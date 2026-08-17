using System.Runtime.CompilerServices;

// 公开 API 之外的内部帧推进逻辑 (LAGroup.Update / OnRegistered) 仅对验证程序集开放,
// 便于在不依赖 CompositionTarget 的情况下做确定性逐帧断言。
[assembly: InternalsVisibleTo("LAE.Perf")]
