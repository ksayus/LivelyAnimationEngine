namespace LAE;

/// <summary>
/// 动画组：按加入顺序保存动作，每帧推进。
/// <para>
/// 默认并行；带序列门标记的动作（由 <c>Then()</c> / <c>Wait()</c> 产生）要等更早步骤的
/// 动作全部完成才开始。同一步骤的子动作（如 <c>Move</c> 的 X/Y）共享步骤号，一起等门、
/// 一起开始，彼此不算前序。遇到还没放行的序列门，后面的动作本帧不能越过。
/// </para>
/// <para>
/// 每帧按固定点方式推进：先推进本帧还没推过的已放行动作，再放行就绪的序列门，重复到
/// 没有动作可推进为止。每个动作每帧最多推一次，且只使用前序完成后剩下的时间预算，
/// 所以同一帧的时间不会被重复计入，也不会一帧快进完整条序列。
/// </para>
/// </summary>
public sealed class LAGroup
{
    private static int _nextId = 1;

    /// <summary>动作及其调度状态，下标与动作列表对应</summary>
    private sealed class ActionState
    {
        public readonly ILAAction Action;
        /// <summary>所属步骤号，序列门只比较步骤号，组合动作共享一个号</summary>
        public readonly int Step;
        /// <summary>是否需要等更早的步骤完成</summary>
        public readonly bool NeedsBarrier;
        public double Elapsed;
        public bool Started;
        /// <summary>序列门是否还没放行</summary>
        public bool Pending;
        /// <summary>零时长等待，放行后立即结算，不必多占一帧</summary>
        public readonly bool IsZeroWait;

        public ActionState(ILAAction action, bool needsBarrier, int step)
        {
            Action = action;
            Step = step;
            NeedsBarrier = needsBarrier;
            Pending = needsBarrier;
            IsZeroWait = action is WaitLA { DurationMs: <= 0 };
        }
    }

    private readonly List<ActionState> _states = new();
    private long[] _lastAdvancedFrame = Array.Empty<long>();
    private long _frameStamp;

    public int Id { get; } = _nextId++;

    public string Name { get; set; } = string.Empty;

    public bool IsCompleted { get; private set; }

    /// <summary>动作数量</summary>
    public int ActionCount => _states.Count;

    public Action? OnComplete { get; set; }

    internal void AddAction(ILAAction action, bool waitForPrevious, int step)
    {
        _states.Add(new ActionState(action, waitForPrevious, step));
        if (_lastAdvancedFrame.Length < _states.Count)
            Array.Resize(ref _lastAdvancedFrame, Math.Max(4, _states.Count * 2));
    }

    /// <summary>重新注册（重播）时复位全部动作进度</summary>
    internal void OnRegistered()
    {
        for (int i = 0; i < _states.Count; i++)
        {
            var s = _states[i];
            s.Elapsed = 0;
            s.Started = false;
            s.Pending = s.NeedsBarrier;
        }
        IsCompleted = false;
    }

    /// <summary>推进本组一帧</summary>
    /// <param name="deltaMs">本帧经过的时间，已经乘过速度倍率（毫秒）</param>
    internal void Update(double deltaMs)
    {
        if (IsCompleted) return;

        int count = _states.Count;
        if (count == 0)
        {
            IsCompleted = true;
            return;
        }

        long stamp = ++_frameStamp;

        // 本帧剩余可用时间。批次之间只能传递"提前完成省下来的余量"，
        // 所以同一帧的时间不会被重复计入，也不会一帧快进完整条序列。
        double remaining = deltaMs;

        // 本帧开始时哪些动作的门已经开了；门只在批次之间重新评估
        var runnable = new bool[count];
        for (int i = 0; i < count; i++)
            runnable[i] = !_states[i].Pending || IsGateOpen(i);

        while (true)
        {
            double batchDelta = remaining;
            double batchElapsed = 0;

            // 推进本批次还没推过的动作
            for (int i = 0; i < count; i++)
            {
                if (!runnable[i]) continue;

                var state = _states[i];
                if (state.Action.IsDone) continue;
                if (_lastAdvancedFrame[i] == stamp) continue;

                _lastAdvancedFrame[i] = stamp;

                if (state.Pending)
                {
                    state.Pending = false;
                    state.Elapsed = 0;
                }

                double elapsed = Advance(state, batchDelta);
                if (elapsed > batchElapsed) batchElapsed = elapsed;
            }

            // 放行就绪的序列门
            bool released = false;
            for (int i = 0; i < count; i++)
            {
                if (runnable[i] || _lastAdvancedFrame[i] == stamp) continue;
                if (!IsGateOpen(i)) continue;
                runnable[i] = true;
                released = true;
            }

            if (!released) break;

            // 只有所有已推进动作都在预算内提前完成，余量才能留给后继动作
            remaining -= batchElapsed;
            if (remaining <= 0) break;
        }

        if (CountDone() >= count)
            IsCompleted = true;
    }

    /// <summary>
    /// 下标 index 的序列门是否就绪：属于更早步骤的动作都已完成。
    /// 同一步骤的子动作不算前序，所以会同时开始。
    /// </summary>
    private bool IsGateOpen(int index)
    {
        if (!_states[index].Pending) return true;

        int myStep = _states[index].Step;
        for (int i = 0; i < _states.Count; i++)
        {
            if (i == index) continue;
            var other = _states[i];
            if (other.Step >= myStep) continue;          // 同步或更晚的步骤不阻塞
            if (!other.Action.IsDone) return false;
        }
        return true;
    }

    /// <summary>
    /// 推进单个已放行的动作。
    /// 时间累加在含延迟的进度上，传给 <see cref="ILAAction.Update"/> 的是动作开始后的
    /// 累计时长（已扣掉延迟），所以动作在帧中途开始时不会跳帧。
    /// </summary>
    /// <returns>
    /// 本批次中该动作占用的帧时间。瞬间完成的动作（回调、零时长等待）返回 0，
    /// 余量可以留给同一帧的后继动作。
    /// </returns>
    private static double Advance(ActionState state, double deltaMs)
    {
        double delay = state.Action.DelayMs;
        double before = state.Elapsed;

        state.Elapsed += deltaMs;

        // 本批次真正经过的时间：跨过延迟点时只算延迟之后的部分
        double elapsed = state.Elapsed - (before > delay ? before : delay);
        if (elapsed < 0) elapsed = 0;
        if (elapsed > deltaMs) elapsed = deltaMs;

        if (state.Elapsed < delay) return elapsed;   // 延迟还没走完，动作尚未开始

        if (!state.Started)
        {
            state.Started = true;
            state.Action.OnStart();
        }

        state.Action.Update(state.Elapsed - delay);

        // 零时长等待在延迟结束处立即结算
        if (state.IsZeroWait && !state.Action.IsDone)
            state.Action.Update(state.Action.DurationMs);

        // 瞬间完成的动作不占帧时间
        return state.Action.DurationMs > 0 ? elapsed : 0;
    }

    private int CountDone()
    {
        int n = 0;
        for (int i = 0; i < _states.Count; i++)
        {
            if (_states[i].Action.IsDone) n++;
        }
        return n;
    }
}
