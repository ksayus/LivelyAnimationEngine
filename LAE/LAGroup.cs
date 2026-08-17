namespace LAE;

/// <summary>
/// 动画组: 持有按加入顺序排列的动作列表, 并在每帧推进它们。
/// <para>
/// 组合语义:
/// <list type="bullet">
/// <item>未标记的动作默认并行开始。</item>
/// <item>带 <see cref="ILAAction.WaitForPrevious"/> 标记的动作 (由 <c>Then()</c> / <c>Wait()</c> 产生)
/// 会等待<b>更早步骤</b>的全部动作完成后才开始。</item>
/// <item>同一步骤内的多个子动作 (如 <c>Move</c> 的 X/Y) 一起等待、一起开始。</item>
/// <item>一旦遇到尚未就绪的序列门, 后续动作本帧不得越过。</item>
/// </list>
/// </para>
/// <para>
/// 每帧以固定点方式推进: 先推进本帧尚未推进过的已放行动作, 再把就绪的序列门放行,
/// 重复至没有动作可推进。每个动作每帧最多推进一次, 且只使用"前序动作完成后剩余的"
/// 时间预算, 因此既不会把同一帧的时间重复计入, 也不会在同一帧内快进整条序列。
/// </para>
/// </summary>
public sealed class LAGroup
{
    private static int _nextId = 1;

    /// <summary>动作及其调度状态, 与动作列表同索引</summary>
    private sealed class ActionState
    {
        public readonly ILAAction Action;
        /// <summary>所属步骤号; 序列门只比较步骤号, 同一组合动作共享一步</summary>
        public readonly int Step;
        /// <summary>是否需要等待更早步骤完成</summary>
        public readonly bool NeedsBarrier;
        public double Elapsed;
        public bool Started;
        /// <summary>序列门是否仍未放行</summary>
        public bool Pending;
        /// <summary>零时长等待, 放行后立即结算, 不必多占一帧</summary>
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

    /// <summary>
    /// 重新注册 (重放) 时复位全部动作进度
    /// </summary>
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

    /// <summary>
    /// 推进本组动画一帧。
    /// </summary>
    /// <param name="deltaMs">本帧经过的、已乘速度倍率的时间 (毫秒)</param>
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

        // 本帧剩余可用时间。批次之间只有"提前完成省下的余量"可以传递,
        // 因此同一帧的时间绝不会被重复计入, 也不会在同一帧内快进整条序列。
        double remaining = deltaMs;

        // 本帧开始时哪些动作的序列门已开; 门只在批次之间重新评估。
        var runnable = new bool[count];
        for (int i = 0; i < count; i++)
            runnable[i] = !_states[i].Pending || IsGateOpen(i);

        while (true)
        {
            double batchDelta = remaining;
            double batchElapsed = 0;

            // ── 推进本批次尚未推进的动作 ──
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

            // ── 放行就绪的序列门 ──
            bool released = false;
            for (int i = 0; i < count; i++)
            {
                if (runnable[i] || _lastAdvancedFrame[i] == stamp) continue;
                if (!IsGateOpen(i)) continue;
                runnable[i] = true;
                released = true;
            }

            if (!released) break;

            // 只有"所有已推进动作都在预算内提前完成"时才把余量传给后继动作
            remaining -= batchElapsed;
            if (remaining <= 0) break;
        }

        if (CountDone() >= count)
            IsCompleted = true;
    }

    /// <summary>
    /// 下标 index 的序列门是否已就绪: 所有属于<b>更早步骤</b>的动作都已完成。
    /// 同一步骤内的子动作彼此不算前序, 因此会同时开始。
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
    /// 推进单个已放行动作。
    /// <para>
    /// 时间累加在包含延迟的进度上; 传给 <see cref="ILAAction.Update"/> 的是
    /// <b>自动作开始起的累计时长</b>(已扣除延迟), 因此动作在帧中途开始时不会跳帧。
    /// </para>
    /// </summary>
    /// <param name="state">动作状态</param>
    /// <param name="deltaMs">本批次可用的时间</param>
    /// <returns>
    /// 本批次中该动作占用掉的帧时间。只有真正需要时长的动作会占用时间;
    /// 瞬间完成的动作 (回调、零时长等待) 返回 0, 余量可留给同一帧的后继动作。
    /// </returns>
    private static double Advance(ActionState state, double deltaMs)
    {
        double delay = state.Action.DelayMs;
        double before = state.Elapsed;

        state.Elapsed += deltaMs;

        // 本批次真正经过的时间: 跨过延迟点时只算延迟之后的那部分
        double elapsed = state.Elapsed - (before > delay ? before : delay);
        if (elapsed < 0) elapsed = 0;
        if (elapsed > deltaMs) elapsed = deltaMs;

        if (state.Elapsed < delay) return elapsed;   // 延迟尚未走完, 动作还未开始

        if (!state.Started)
        {
            state.Started = true;
            state.Action.OnStart();
        }

        state.Action.Update(state.Elapsed - delay);

        // 零时长等待在延迟结束处立即结算
        if (state.IsZeroWait && !state.Action.IsDone)
            state.Action.Update(state.Action.DurationMs);

        // 瞬间完成的动作不占用帧时间
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
