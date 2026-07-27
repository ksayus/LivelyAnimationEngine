namespace LAE;

public sealed class LAGroup
{
    private static int _nextId = 1;

    private readonly List<ActionState> _states = new();

    public int Id { get; } = _nextId++;

    public string Name { get; set; } = string.Empty;

    public bool IsCompleted { get; private set; }

    public Action? OnComplete { get; set; }

    private sealed class ActionState
    {
        public ILAAction Action;
        public double Elapsed;
        public bool Started;
        public bool Pending;

        public ActionState(ILAAction action) => Action = action;
    }

    internal void AddAction(ILAAction action)
    {
        _states.Add(new ActionState(action)
        {
            Pending = action.WaitForPrevious
        });
    }

    internal void OnRegistered()
    {
        foreach (var s in  _states)
        {
            s.Elapsed = 0;
            s.Started = false;
            s.Pending = s.Action.WaitForPrevious;
        }
        IsCompleted = false;
    }

    internal void Update(double deltaMs)
    {
        if (IsCompleted) return;

        bool precedingClear = true; // 前面的动作是否全部完成

        for (int i = 0;i<_states.Count;i++)
        {
            var state = _states[i];

            if (state.Action.IsDone)
            {
                continue;
            }

            // 序列门: Pending 的动作需要前面全部完成
            if (state.Pending)
            {
                if (!precedingClear)
                    break;  // 前面还没完,本组暂停后继续
                // 解除 Pending, 开始计时
                state.Pending = false;
                state.Elapsed = 0;
            }

            // 非 Pending 动作开始计时
            precedingClear = false; // 有动作在运行, 后续 Pending 必须等
            state.Elapsed += deltaMs;

            if (state.Elapsed < state.Action.DelayMs)
                continue;

            // 首次执行
            if (!state.Started)
            {
                state.Started = true;
                state.Action.OnStart();
            }

            // 执行动画帧 (传入去掉延迟之后的的时间)
            state.Action.Update(state.Elapsed - state.Action.DelayMs);
        }

        bool allDone = _states.TrueForAll(s => s.Action.IsDone);
        if (allDone)
        {
            IsCompleted = true;
            OnComplete?.Invoke();
        }
    }
}