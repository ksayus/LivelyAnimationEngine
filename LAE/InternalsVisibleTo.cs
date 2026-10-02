using System.Runtime.CompilerServices;

// 帧推进相关的内部成员（LAGroup.Update / OnRegistered）只对验证程序集开放，
// 这样才能脱离 CompositionTarget 做确定性的逐帧断言。
[assembly: InternalsVisibleTo("LAE.Perf")]
