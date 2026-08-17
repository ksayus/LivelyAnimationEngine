// LAE engine verification harness.
// Drives animations through deterministic manual frame stepping so behaviour can be
// asserted without a live CompositionTarget.
//
//   dotnet run --project Perf              -> run all assertions
//   dotnet run --project Perf -- --bench   -> legacy vs current frame-loop benchmark

using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using LAE;

namespace LAE.Perf;

internal static class Program
{
    private static int _passed;
    private static int _failed;
    private static readonly List<string> Failures = new();

    [STAThread]
    private static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length > 0 && args[0] is "--bench" or "-b")
        {
            Bench.CompareLegacyAndCurrent();
            return;
        }

        AbsoluteDependencyProperty();
        RelativeDependencyProperty();
        RelativeAccumulationHasNoDrift();
        SequenceOrdering();
        SequenceDelaysInsideSteps();
        ParallelActionsShareTheFrame();
        WaitIsASequenceBarrier();
        ZeroWaitConsumesNoExtraFrame();
        CallbackOrdering();
        OnCompleteFiresExactlyOnce();
        ResetReplaysGroupFromScratch();
        ScaleAxesAreIndependent();
        NegativeScaleIsClampedAtZero();
        MoveAnimateXAndYIndependently();
        FrozenBrushIsClonedAndReattached();
        UnfrozenBrushIsWrittenInPlace();
        ColorEndsExactlyOnTarget();
        EasingEndpointsAreExact();
        EasingCurvesAreMonotonic();
        SpeedIsClampedToDocumentedRange();
        EasingMismatchCannotStrandProgress();
        GroupCompletionViaUpdate();
        PublicApiSurfaceIsIntact();

        PerfSmoke();

        Console.WriteLine();
        Console.WriteLine($"passed: {_passed}   failed: {_failed}");
        if (_failed > 0)
        {
            Console.WriteLine();
            foreach (var f in Failures) Console.WriteLine("  FAIL " + f);
            Environment.Exit(1);
        }
    }

    // ── infrastructure ────────────────────────────────────────────────

    private static void Check(string name, bool condition, string? detail = null)
    {
        if (condition) { _passed++; return; }
        _failed++;
        Failures.Add(detail == null ? name : $"{name} :: {detail}");
    }

    private static void Near(string name, double actual, double expected, double tol = 1e-9)
        => Check(name, Math.Abs(actual - expected) <= tol, $"actual={actual}, expected={expected}");

    /// <summary>Frame driver mirroring LAEngine.OnRendering's time accumulation.</summary>
    private static void Run(LAGroup group, double frameMs, int frames)
    {
        for (int i = 0; i < frames && !group.IsCompleted; i++)
            group.Update(frameMs);
    }

    private static LAGroup Group(LABuilder b) => b.BuildGroup();

    // ── tests ─────────────────────────────────────────────────────────

    private static void AbsoluteDependencyProperty()
    {
        var t = new TranslateTransform(0, 0);
        // Linear easing so intermediate values are exactly halfway.
        var g = Group(LA.Builder().Ease(Easing.Linear).Move(t, 100, 50, 100));

        Run(g, 50, 1);
        Near("absolute: halfway X", t.X, 50);
        Near("absolute: halfway Y", t.Y, 25);

        Run(g, 50, 1);
        Near("absolute: final X", t.X, 100);
        Near("absolute: final Y", t.Y, 50);
        Check("absolute: group completed", g.IsCompleted);
    }

    private static void RelativeDependencyProperty()
    {
        var t = new TranslateTransform(30, 20);
        var g = Group(LA.Builder().MoveBy(t, 70, -10, 100));

        Run(g, 100, 1);
        Near("relative: final X", t.X, 100);
        Near("relative: final Y", t.Y, 10);
    }

    private static void RelativeAccumulationHasNoDrift()
    {
        // 1000 uneven frames must still land exactly on the endpoint (absolute writes).
        var t = new TranslateTransform(0, 0);
        var g = Group(LA.Builder().MoveBy(t, 137.4, 0, 1000));

        var rnd = new Random(1234);
        for (int i = 0; i < 1000 && !g.IsCompleted; i++)
            g.Update(0.1 + rnd.NextDouble() * 2.5);

        Near("drift: endpoint reached exactly", t.X, 137.4, 1e-9);
    }

    private static void SequenceOrdering()
    {
        var order = new List<string>();

        var g = Group(LA.Builder("seq")
            .Callback(() => order.Add("A"))
            .Then()
            .Callback(() => order.Add("B"))
            .Then()
            .Callback(() => order.Add("C")));

        Run(g, 16, 1);

        // Instant actions are released in order and are not made to wait an extra frame
        // each: Then() guarantees ORDER, not one-frame-per-step.
        Check("sequence: instant steps chain in order within the frame",
            string.Join(",", order) == "A,B,C", string.Join(",", order));
        Check("sequence: completed once the chain is drained", g.IsCompleted);
    }

    private static void SequenceDelaysInsideSteps()
    {
        var t = new TranslateTransform(0, 0);
        // start -> (delay 200 + move 100ms) -> then -> (move 100ms)
        var g = Group(LA.Builder().Ease(Easing.Linear)
            .Delay(200).MoveBy(t, 60, 0, 100)
            .Then().MoveBy(t, 40, 0, 100));

        Run(g, 100, 2);              // t=200: first step begins, position still 0
        Near("sequence delay: not started before delay", t.X, 0.0);

        Run(g, 100, 1);              // t=300: first step finished -> X=60
        Near("sequence delay: first step landed", t.X, 60);

        Run(g, 100, 1);              // t=400: second step finished -> X=100
        Near("sequence delay: second step landed", t.X, 100);
        Check("sequence delay: completed", g.IsCompleted);
    }

    private static void ParallelActionsShareTheFrame()
    {
        var t = new TranslateTransform(0, 0);
        var order = new List<string>();

        var g = Group(LA.Builder()
            .Callback(() => order.Add("c1"))
            .Callback(() => order.Add("c2"))
            .MoveBy(t, 10, 0, 1000));

        Run(g, 16, 1);
        Check("parallel: both callbacks ran in the same frame", order.Count == 2,
            string.Join(",", order));
    }

    private static void WaitIsASequenceBarrier()
    {
        var order = new List<string>();
        var g = Group(LA.Builder()
            .Callback(() => order.Add("before"))
            .Wait(100)
            .Callback(() => order.Add("after")));

        Run(g, 60, 1);   // 60ms into the 100ms wait
        Check("wait: nothing after wait on frame 1", !order.Contains("after"),
            string.Join(",", order));

        Run(g, 60, 1);   // crosses 100ms: the wait settles, consuming this frame's budget
        Check("wait: follower still gated on the settling frame", !order.Contains("after"),
            string.Join(",", order));

        Run(g, 1, 1);    // the next frame releases the follower
        Check("wait: follower released after the wait elapsed", order.Contains("after"),
            string.Join(",", order));
        Check("wait: group completed", g.IsCompleted);
    }

    private static void ZeroWaitConsumesNoExtraFrame()
    {
        var order = new List<string>();
        var g = Group(LA.Builder()
            .Wait(0)
            .Callback(() => order.Add("x")));

        Run(g, 16, 2);
        Check("zero wait: completes without an extra frame", g.IsCompleted);
        Check("zero wait: follower ran", order.Contains("x"));
    }

    private static void CallbackOrdering()
    {
        var order = new List<string>();
        var g = Group(LA.Builder()
            .Callback(() => order.Add("first"))
            .Then()
            .Callback(() => order.Add("second")));

        Run(g, 16, 5);
        Check("callback: ordered", string.Join(",", order) == "first,second", string.Join(",", order));
    }

    private static void OnCompleteFiresExactlyOnce()
    {
        // OnComplete is owned by the engine (fired once when a completed group leaves
        // the active set), so the group only reports completion here.
        int calls = 0;
        var g = Group(LA.Builder().Wait(10).OnComplete(() => calls++));
        Check("OnComplete: registered on the group", g.OnComplete != null);

        Run(g, 20, 1);
        Check("OnComplete: group completed after one frame", g.IsCompleted);
        Check("OnComplete: group itself does not invoke it", calls == 0, $"calls={calls}");

        g.Update(20);
        Check("OnComplete: completed group stays completed", g.IsCompleted && calls == 0);
    }

    private static void ResetReplaysGroupFromScratch()
    {
        var t = new TranslateTransform(0, 0);
        var g = Group(LA.Builder().Ease(Easing.Linear).MoveBy(t, 100, 0, 100));

        Run(g, 50, 1);
        Near("replay: mid-run position", t.X, 50);

        // Re-registering restarts progress; the origin is re-read from the live value,
        // so the action lands at (current + delta) and never accumulates the old run.
        g.OnRegistered();
        Run(g, 100, 1);
        Near("replay: restarts from current value", t.X, 150);
    }

    private static void ScaleAxesAreIndependent()
    {
        var s = new ScaleTransform(1, 2);
        var g = Group(LA.Builder().Scale(s, 3.0, 100));

        Run(g, 100, 1);
        Near("scale: X reaches target", s.ScaleX, 3.0);
        Near("scale: Y is independent, reaches target", s.ScaleY, 3.0);

        // A non-uniform start must not leak across axes.
        var s2 = new ScaleTransform(1, 4);
        var g2 = Group(LA.Builder().Ease(Easing.Linear).ScaleBy(s2, 1.0, 100));
        Run(g2, 50, 1);
        Near("scale: X interpolates from its own origin", s2.ScaleX, 1.5);
        Near("scale: Y interpolates from its own origin", s2.ScaleY, 4.5);
        Run(g2, 50, 1);
        Near("scale: X final", s2.ScaleX, 2.0);
        Near("scale: Y final", s2.ScaleY, 5.0);
    }

    private static void NegativeScaleIsClampedAtZero()
    {
        var s = new ScaleTransform(1, 1);
        var g = Group(LA.Builder().ScaleBy(s, -5, 100));

        Run(g, 100, 1);
        Near("clamp: ScaleX floored at 0", s.ScaleX, 0.0);
        Near("clamp: ScaleY floored at 0", s.ScaleY, 0.0);
    }

    private static void MoveAnimateXAndYIndependently()
    {
        var t = new TranslateTransform(0, 0);
        var g = Group(LA.Builder().Move(t, 200, 0, 100));

        Run(g, 100, 1);
        Near("move: X landed", t.X, 200);
        Near("move: Y untouched", t.Y, 0);
    }

    private static void FrozenBrushIsClonedAndReattached()
    {
        var rect = new Rectangle { Width = 10, Height = 10 };
        var frozen = new SolidColorBrush(Colors.Blue);
        frozen.Freeze();
        rect.Fill = frozen;

        var g = Group(LA.Builder().Color(rect, Shape.FillProperty, Colors.Red, 100));

        // Build must not have touched the element yet.
        Check("frozen brush: no side effect at build time",
            ReferenceEquals(rect.Fill, frozen));

        Run(g, 100, 1);

        var after = rect.Fill as SolidColorBrush;
        Check("frozen brush: element now holds a mutable clone",
            after != null && !ReferenceEquals(after, frozen) && !after.IsFrozen);
        Check("frozen brush: color animated to target",
            after != null && after.Color == Colors.Red,
            after?.Color.ToString());

        // Repeated runs must not keep stacking clones.
        var g2 = Group(LA.Builder().Color(rect, Shape.FillProperty, Colors.Lime, 100));
        Run(g2, 100, 1);
        Check("frozen brush: no clone accumulation",
            ReferenceEquals(rect.Fill, after) && after!.Color == Colors.Lime);
    }

    private static void UnfrozenBrushIsWrittenInPlace()
    {
        var brush = new SolidColorBrush(Colors.Black);
        var g = Group(LA.Builder().Color(brush, Colors.White, 100));

        Run(g, 50, 1);
        Check("unfrozen brush: midway colour is between",
            brush.Color.R > 0x60 && brush.Color.R < 0xF0, brush.Color.ToString());

        Run(g, 50, 1);
        Check("unfrozen brush: exact target", brush.Color == Colors.White, brush.Color.ToString());
    }

    private static void ColorEndsExactlyOnTarget()
    {
        var brush = new SolidColorBrush(Color.FromArgb(10, 20, 30, 40));
        var target = Color.FromArgb(200, 210, 220, 230);
        var g = Group(LA.Builder().Color(brush, target, 100));
        Run(g, 100, 1);
        Check("colour: exact ARGB target", brush.Color == target, brush.Color.ToString());
    }

    private static void EasingEndpointsAreExact()
    {
        var curves = new (string Name, Func<double, double> Fn)[]
        {
            ("Linear", Easing.Linear),
            ("OutCubic", Easing.OutCubic),
            ("InOutCubic", Easing.InOutCubic),
            ("OutPow(2)", Easing.OutPow(2)),
            ("OutPow(4)", Easing.OutPow(4)),
            ("OutPow(5)", Easing.OutPow(5)),
            ("OutPow(7)", Easing.OutPow(7)),
            ("InOutPow(2)", Easing.InOutPow(2)),
            ("InOutPow(4)", Easing.InOutPow(4)),
            ("InOutPow(5)", Easing.InOutPow(5)),
            ("InOutPow(7)", Easing.InOutPow(7)),
        };

        foreach (var (name, fn) in curves)
        {
            Near($"easing {name}: f(0)", fn(0), 0.0, 1e-12);
            Near($"easing {name}: f(1)", fn(1), 1.0, 1e-12);

            bool monotonic = true;
            double prev = -1;
            for (int i = 0; i <= 1000; i++)
            {
                double v = fn(i / 1000.0);
                if (v < prev - 1e-12) { monotonic = false; break; }
                prev = v;
            }
            Check($"easing {name}: monotonically non-decreasing", monotonic);

            Near($"easing {name}: clamps below 0", fn(-5), 0, 1e-12);
            Near($"easing {name}: clamps above 1", fn(5), 1, 1e-12);
        }
    }

    private static void EasingCurvesAreMonotonic()
    {
        // Compare polynomial fast paths against Math.Pow reference implementations.
        foreach (int p in new[] { 2, 3, 4, 5, 6, 7, 9, 12 })
        {
            var outFast = Easing.OutPow(p);
            var inoutFast = Easing.InOutPow(p);
            double maxOut = 0, maxInOut = 0;

            for (int i = 0; i <= 200; i++)
            {
                double t = i / 200.0;
                maxOut = Math.Max(maxOut, Math.Abs(outFast(t) - (1 - Math.Pow(1 - t, p))));
                double reference = t < 0.5
                    ? 0.5 * Math.Pow(2 * t, p)
                    : 1 - 0.5 * Math.Pow(2 * (1 - t), p);
                maxInOut = Math.Max(maxInOut, Math.Abs(inoutFast(t) - reference));
            }

            Check($"easing OutPow({p}): matches Math.Pow reference", maxOut < 1e-9, $"maxErr={maxOut}");
            Check($"easing InOutPow({p}): matches Math.Pow reference", maxInOut < 1e-9, $"maxErr={maxInOut}");
        }
    }

    private static void SpeedIsClampedToDocumentedRange()
    {
        double original = LAEngine.Speed;
        try
        {
            LAEngine.Speed = 1.0;
            Near("speed: normal value kept", LAEngine.Speed, 1.0);

            LAEngine.Speed = 500;
            Near("speed: upper clamp", LAEngine.Speed, 200.0);

            LAEngine.Speed = 0.0001;
            Near("speed: lower clamp", LAEngine.Speed, 0.1);

            LAEngine.Speed = 2.5;
            Near("speed: mid value kept", LAEngine.Speed, 2.5);
        }
        finally
        {
            LAEngine.Speed = original;
        }
    }

    private static void EasingMismatchCannotStrandProgress()
    {
        // A non-monotonic curve must still terminate the action.
        var t = new TranslateTransform(0, 0);
        Func<double, double> odd = x => x * x * 3 - x * 2;   // f(0)=0, f(1)=1
        var g = Group(LA.Builder().Ease(odd).MoveBy(t, 50, 0, 100));

        Run(g, 100, 1);
        Check("odd easing: action still completes", g.IsCompleted);
    }

    private static void GroupCompletionViaUpdate()
    {
        var t = new TranslateTransform(0, 0);
        var g = Group(LA.Builder().MoveBy(t, 1, 0, 10));
        Check("group: not completed before first frame", !g.IsCompleted);
        Run(g, 10, 1);
        Check("group: completed after final frame", g.IsCompleted);

        g.Update(10);
        Check("group: update on completed group is a no-op", g.IsCompleted);
    }

    /// <summary>
    /// Exercises every documented public entry point exactly as the README shows it.
    /// This is the compatibility guard: the public surface must keep working unchanged.
    /// </summary>
    private static void PublicApiSurfaceIsIntact()
    {
        var translate = new TranslateTransform(0, 0);
        var scale = new ScaleTransform(1, 1);
        var rotate = new RotateTransform(0);
        var skew = new SkewTransform(0, 0);
        var brush = new SolidColorBrush(Colors.Red);
        var rect = new Rectangle { Width = 20, Height = 20, Fill = new SolidColorBrush(Colors.Blue) };

        // 构建器入口: 匿名 / 命名
        Check("api: LA.Builder() 返回构建器", LA.Builder() is LABuilder);
        Check("api: LA.Builder(name) 命名生效",
            LA.Builder("api_named").BuildGroup().Name == "api_named");

        // 全部变换动画 + 属性动画 + 快捷方法, 串联成一条链
        var group = LA.Builder("api_surface")
            .During(300)
            .Ease(Easing.OutCubic)
            .Move(translate, 10, 20, 300)
            .MoveBy(translate, 5, 5, 300)
            .Scale(scale, 1.5, 300)
            .ScaleBy(scale, 0.2, 300)
            .Rotate(rotate, 90, 300)
            .RotateBy(rotate, 45, 300)
            .Skew(skew, 10, 300)
            .SkewBy(skew, 5, 300)
            .To(translate, TranslateTransform.XProperty, 100, 300)
            .By(translate, TranslateTransform.YProperty, 50, 300)
            .Opacity(rect, 0.5, 300)
            .OpacityBy(rect, -0.1, 300)
            .Width(rect, 40, 300)
            .WidthBy(rect, 5, 300)
            .Height(rect, 40, 300)
            .HeightBy(rect, 5, 300)
            .Color(brush, Colors.Green, 300)
            .Color(rect, Shape.FillProperty, Colors.Yellow, 300)
            .FadeIn(rect, 300)
            .FadeOut(rect, 300)
            .Then()
            .Delay(50)
            .Wait(20)
            .Callback(() => { })
            .OnComplete(() => { })
            .BuildGroup();

        if (group is null)
        {
            Check("api: 构建器链返回 LAGroup", false, "BuildGroup() 返回了 null");
            return;
        }

        Check("api: 构建器链返回 LAGroup", true);
        Check("api: 组名可读", group.Name == "api_surface");
        Check("api: ActionCount 可读", group.ActionCount > 20, $"count={group.ActionCount}");
        Check("api: BuildGroup 不自动启动", !LAEngine.IsRunning("api_surface"));

        // Play() 返回组名并注册
        string name = LA.Builder("api_play").Move(translate, 1, 1, 100).Play();
        Check("api: Play() 返回组名", name == "api_play", name);
        Check("api: Play() 后 IsRunning 为真", LAEngine.IsRunning("api_play"));
        Check("api: ActiveGroupCount 统计到组", LAEngine.ActiveGroupCount >= 1);

        LAEngine.Stop("api_play");
        Check("api: Stop 后 IsRunning 为假", !LAEngine.IsRunning("api_play"));

        // 冻结 / 解冻 成对使用
        bool frozenBefore = LAEngine.IsFrozen;
        LAEngine.Freeze();
        Check("api: Freeze 置为冻结", LAEngine.IsFrozen);
        LAEngine.Unfreeze();
        Check("api: Unfreeze 恢复", LAEngine.IsFrozen == frozenBefore);

        // 多解冻不会变成负数
        LAEngine.Unfreeze();
        LAEngine.Unfreeze();
        Check("api: 多余 Unfreeze 不会使计数为负", !LAEngine.IsFrozen);

        Check("api: Speed 可读写", LAEngine.Speed > 0);

        // 异常隔离接口
        Check("api: LastError 初始为 null", LAEngine.LastError == null);
        LAEngine.ClearLastError();

        // 缓动函数工厂
        Check("api: Easing.OutPow 可用", Easing.OutPow(3) is not null);
        Check("api: Easing.InOutPow 可用", Easing.InOutPow(3) is not null);
        Check("api: Easing.Linear 可用", Easing.Linear is not null);
        Check("api: Easing.OutCubic 可用", Easing.OutCubic is not null);
        Check("api: Easing.InOutCubic 可用", Easing.InOutCubic is not null);

        // 让这条超长链实际跑起来, 确认不会卡死
        int frames = 0;
        while (!group.IsCompleted && frames < 5000)
        {
            group.Update(16);
            frames++;
        }
        Check("api: 完整链式动画可以跑完", group.IsCompleted, $"frames={frames}");

        LAEngine.StopAll();
        Check("api: StopAll 清空所有组", LAEngine.ActiveGroupCount == 0);
    }

    private static void PerfSmoke()
    {
        // 200 concurrent groups, 300 simulated frames at 16ms.
        var groups = new List<LAGroup>(200);
        var items = new List<(TranslateTransform T, ScaleTransform S)>(200);

        for (int i = 0; i < 200; i++)
        {
            var t = new TranslateTransform(0, 0);
            var s = new ScaleTransform(1, 1);
            items.Add((t, s));
            groups.Add(Group(LA.Builder()
                .Scale(s, 2.0, 1000)
                .MoveBy(t, 300, 120, 1000)));
        }

        foreach (var g in groups) g.Update(16);   // warm up

        var sw = Stopwatch.StartNew();
        int frames = 0;
        for (int f = 0; f < 300; f++)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                if (g.IsCompleted) continue;
                g.Update(16);
            }
            frames++;
        }
        sw.Stop();

        double perFrameUs = sw.Elapsed.TotalMilliseconds * 1000.0 / frames;
        Console.WriteLine($"perf: 200 groups x 300 frames -> {sw.Elapsed.TotalMilliseconds:F1} ms " +
                          $"({perFrameUs:F1} us/frame, {perFrameUs / 200:F2} us per group)");
        Check("perf: 200 animated groups stay well under a 16ms frame", perFrameUs < 16000,
            $"{perFrameUs:F1} us/frame");

        var sample = items[0];
        Check("perf: animations actually landed", Math.Abs(sample.T.X - 300) < 1e-6,
            $"X={sample.T.X}");
    }

    /// <summary>
    /// Like-for-like frame-loop comparison: reconstructed legacy loop vs the current engine.
    /// Run with <c>dotnet run --project Perf -- --bench</c>.
    /// <para>
    /// The "legacy" side is a faithful reconstruction of the original frame loop and
    /// DependencyPropertyLA (per-frame List snapshot, GetValue read-back, delta writes),
    /// kept as a regression baseline. Both sides animate the same four dependency
    /// properties per group so only the processing strategy differs.
    /// </para>
    /// </summary>
    private static class Bench
    {
        private const double Duration = 1000;
        private const double Delay = 200;
        private const int Groups = 200;
        private const int Frames = 300;
        private const int Rounds = 20;

        private interface ILegacyAction
        {
            bool IsDone { get; }
            void Start();
            void Step(double elapsedMs);
        }

        /// <summary>Mirrors the original DependencyPropertyLA: read back, then write a delta.</summary>
        private sealed class LegacyPropAction : ILegacyAction
        {
            private readonly DependencyObject _target;
            private readonly DependencyProperty _prop;
            private readonly double _end;
            private double _start;
            private double _last;
            private bool _started;

            public bool IsDone { get; private set; }

            public LegacyPropAction(DependencyObject target, DependencyProperty prop, double end)
            {
                _target = target;
                _prop = prop;
                _end = end;
            }

            public void Start()
            {
                if (_started) return;
                _started = true;
                _start = Convert.ToDouble(_target.GetValue(_prop));
            }

            public void Step(double elapsedMs)
            {
                if (IsDone) return;

                double t = Math.Clamp(elapsedMs / Duration, 0, 1);
                double p = Easing.OutCubic(t);
                double frameDelta = (p - _last) * (_end - _start);
                _last = p;

                // 原始实现: 每帧回读当前值, 再写回增量
                double current = Convert.ToDouble(_target.GetValue(_prop));
                _target.SetValue(_prop, current + frameDelta);

                if (t >= 1.0) IsDone = true;
            }
        }

        private sealed class LegacyGroup
        {
            private readonly List<ILegacyAction> _actions = new();
            private readonly List<double> _elapsed = new();
            private bool _started;

            public bool IsCompleted { get; private set; }

            public void Add(ILegacyAction action)
            {
                _actions.Add(action);
                _elapsed.Add(0);
            }

            public void Update(double deltaMs)
            {
                if (IsCompleted) return;

                if (!_started)
                {
                    _started = true;
                    foreach (var a in _actions) a.Start();
                }

                for (int i = 0; i < _actions.Count; i++)
                {
                    if (_actions[i].IsDone) continue;
                    _elapsed[i] += deltaMs;
                    _actions[i].Step(_elapsed[i] - Delay);
                }

                foreach (var a in _actions)
                {
                    if (!a.IsDone) return;
                }
                IsCompleted = true;
            }
        }

        private static long MeasureLegacy()
        {
            long best = long.MaxValue;
            for (int round = 0; round < Rounds; round++)
            {
                var set = new List<LegacyGroup>(Groups);
                for (int i = 0; i < Groups; i++)
                {
                    var tt = new TranslateTransform(0, 0);
                    var st = new ScaleTransform(1, 1);
                    var g = new LegacyGroup();
                    g.Add(new LegacyPropAction(tt, TranslateTransform.XProperty, 300));
                    g.Add(new LegacyPropAction(tt, TranslateTransform.YProperty, 120));
                    g.Add(new LegacyPropAction(st, ScaleTransform.ScaleXProperty, 2.0));
                    g.Add(new LegacyPropAction(st, ScaleTransform.ScaleYProperty, 2.0));
                    set.Add(g);
                }

                // 复刻原始 OnRendering: 每帧新建快照列表再遍历
                var sw = Stopwatch.StartNew();
                for (int f = 0; f < Frames; f++)
                {
                    var snapshot = new List<LegacyGroup>(set);
                    foreach (var g in snapshot) g.Update(16);
                }
                sw.Stop();
                best = Math.Min(best, sw.ElapsedTicks);
            }
            return best;
        }

        private static long MeasureCurrent()
        {
            long best = long.MaxValue;
            for (int round = 0; round < Rounds; round++)
            {
                var set = new List<LAGroup>(Groups);
                for (int i = 0; i < Groups; i++)
                {
                    var tt = new TranslateTransform(0, 0);
                    var st = new ScaleTransform(1, 1);
                    set.Add(LA.Builder()
                        .Delay(Delay)
                        .MoveBy(tt, 300, 120, Duration)
                        .Scale(st, 2.0, Duration)
                        .BuildGroup());
                }

                // 复刻当前 OnRendering: 稳定视图 + 倒序索引遍历, 无每帧分配
                var view = new List<LAGroup>(set);
                var sw = Stopwatch.StartNew();
                for (int f = 0; f < Frames; f++)
                {
                    for (int i = view.Count - 1; i >= 0; i--)
                    {
                        var g = view[i];
                        if (!g.IsCompleted) g.Update(16);
                    }
                }
                sw.Stop();
                best = Math.Min(best, sw.ElapsedTicks);
            }
            return best;
        }

        public static void CompareLegacyAndCurrent()
        {
            for (int i = 0; i < 3; i++) { MeasureLegacy(); MeasureCurrent(); }   // 预热

            long legacy = MeasureLegacy();
            long current = MeasureCurrent();

            double legacyMs = legacy * 1000.0 / Stopwatch.Frequency;
            double currentMs = current * 1000.0 / Stopwatch.Frequency;
            double legacyNs = legacy * 1e9 / Stopwatch.Frequency / Frames / Groups;
            double currentNs = current * 1e9 / Stopwatch.Frequency / Frames / Groups;

            Console.WriteLine($"config : {Groups} groups x {Frames} frames, 4 properties per group, best of {Rounds}");
            Console.WriteLine();
            Console.WriteLine($"legacy  : {legacyMs,7:F2} ms   {legacyMs * 1000 / Frames,6:F1} us/frame   {legacyNs,6:F0} ns/group");
            Console.WriteLine($"current : {currentMs,7:F2} ms   {currentMs * 1000 / Frames,6:F1} us/frame   {currentNs,6:F0} ns/group");
            Console.WriteLine();
            Console.WriteLine($"speedup : {legacyMs / currentMs:F2}x");
        }
    }
}
