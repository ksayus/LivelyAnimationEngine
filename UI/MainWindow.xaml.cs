using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using LAE;

namespace UI;

/// <summary>
/// LAE 交互式演示台：左侧是按类别分组的演示目录，中间是动画舞台，
/// 右侧实时显示当前演示用到的流式调用和运行日志。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>一个可播放的演示项</summary>
    private sealed record Demo(
        string Name,
        string Category,
        string Description,
        string Code,
        Action Run);

    private readonly List<Demo> _demos = new();
    private readonly Dictionary<string, Button> _demoButtons = new();

    // 舞台元素
    private Rectangle _card = null!;
    private Rectangle _swatch = null!;
    private SolidColorBrush _cardBrush = null!;
    private SolidColorBrush _swatchBrush = null!;
    private Ellipse _opacityDot = null!;
    private Border _sizeBox = null!;

    // 变换
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly SkewTransform _skew = new(0, 0);
    private readonly RotateTransform _rotate = new(0);
    private readonly TranslateTransform _translate = new(0, 0);

    private Demo? _selected;
    private Demo? _lastPlayed;

    // 帧率与状态采样
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastFrames;
    private double _lastSampleMs;
    private DispatcherTimer _timer = null!;

    private static readonly SolidColorBrush CardBase = new(Color.FromRgb(0x34, 0x98, 0xDB));
    private static readonly SolidColorBrush SwatchBase = new(Color.FromRgb(0xE7, 0x4C, 0x3C));

    public MainWindow()
    {
        InitializeComponent();

        BuildStage();
        BuildDemos();
        BuildDemoList();

        CompositionTarget.Rendering += OnFrame;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();

        Loaded += (_, _) =>
        {
            Log("LAE 演示台已就绪");
            Log($"共 {_demos.Count} 个演示，覆盖变换 / 属性 / 组合 / 缓动 / 引擎控制 / 压力测试");
            Log("点击左侧任意演示即可直接在舞台上看到效果");
            Select(_demos[0]);
        };
        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= OnFrame;
            _timer.Stop();
            LAEngine.StopAll();
        };
    }

    // 舞台

    private void BuildStage()
    {
        var transformGroup = new TransformGroup();
        transformGroup.Children.Add(_scale);
        transformGroup.Children.Add(_skew);
        transformGroup.Children.Add(_rotate);
        transformGroup.Children.Add(_translate);

        _cardBrush = CardBase.Clone();
        _card = new Rectangle
        {
            Width = 104,
            Height = 104,
            RadiusX = 16,
            RadiusY = 16,
            Fill = _cardBrush,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = transformGroup,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0x34, 0x98, 0xDB),
                BlurRadius = 26,
                ShadowDepth = 0,
                Opacity = 0.55
            }
        };

        _swatchBrush = SwatchBase.Clone();
        _swatch = new Rectangle
        {
            Width = 76,
            Height = 76,
            RadiusX = 12,
            RadiusY = 12,
            Fill = _swatchBrush,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };

        _opacityDot = new Ellipse
        {
            Width = 86,
            Height = 86,
            Fill = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71)),
            RenderTransformOrigin = new Point(0.5, 0.5)
        };

        _sizeBox = new Border
        {
            Width = 96,
            Height = 96,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromRgb(0x9B, 0x59, 0xB6))
        };

        // 舞台用绝对定位，窗口尺寸变化时重新排布
        stage.Children.Add(_card);
        stage.Children.Add(_swatch);
        stage.Children.Add(_opacityDot);
        stage.Children.Add(_sizeBox);

        AddCaption("主卡片", _card);
        AddCaption("颜色块", _swatch);
        AddCaption("不透明度", _opacityDot);
        AddCaption("尺寸盒", _sizeBox);

        stage.SizeChanged += (_, _) => LayoutStage();
        LayoutStage();
    }

    private void AddCaption(string text, FrameworkElement target)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5D, 0x68, 0x78)),
            Tag = target
        };
        stage.Children.Add(label);
    }

    /// <summary>按舞台尺寸摆放四个演示目标和标题，避免被裁切</summary>
    private void LayoutStage()
    {
        double w = stage.ActualWidth;
        double h = stage.ActualHeight;
        if (w < 200 || h < 200) return;

        double cx = w * 0.5;
        double cy = h * 0.46;

        Place(_card, cx - _card.Width / 2, cy - _card.Height / 2);
        Place(_opacityDot, cx - _opacityDot.Width / 2, cy + 104);
        Place(_sizeBox, cx - 210 - _sizeBox.Width / 2, cy - _sizeBox.Height / 2);
        Place(_swatch, cx + 210 - _swatch.Width / 2, cy - _swatch.Height / 2);

        foreach (var label in stage.Children.OfType<TextBlock>())
        {
            if (label.Tag is not FrameworkElement target) continue;
            Canvas.SetLeft(label, Canvas.GetLeft(target));
            Canvas.SetTop(label, Canvas.GetTop(target) - 17);
        }
    }

    private static void Place(UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
    }

    // 演示目录

    private void BuildDemos()
    {
        void Add(string category, string name, string description, string code, Action run)
            => _demos.Add(new Demo(name, category, description, code, run));

        // 变换动画
        Add("变换动画", "相对位移 MoveBy", "TranslateTransform 相对移动",
            """
            LA.Builder("move_by")
                .During(600)
                .Ease(Easing.OutCubic)
                .MoveBy(_translate, 180, 0)
                .Play();
            """,
            () => LA.Builder("move_by").During(600)
                    .MoveBy(_translate, 180, 0).OnComplete(RefreshStatus).Play());

        Add("变换动画", "绝对位移 Move", "TranslateTransform 移动到绝对坐标",
            """
            LA.Builder("move_to")
                .Move(_translate, 120, -60, 600)
                .Play();
            """,
            () => LA.Builder("move_to").Move(_translate, 120, -60, 600)
                    .OnComplete(RefreshStatus).Play());

        Add("变换动画", "绝对缩放 Scale", "ScaleX / ScaleY 同步缩放到 1.8x",
            """
            LA.Builder("scale")
                .Scale(_scale, 1.8, 450)
                .Play();
            """,
            () => LA.Builder("scale").Scale(_scale, 1.8, 450)
                    .OnComplete(RefreshStatus).Play());

        Add("变换动画", "相对缩放 ScaleBy", "在当前基础上缩放一个增量",
            """
            LA.Builder("scale_by")
                .ScaleBy(_scale, 0.45, 400)
                .Play();
            """,
            () => LA.Builder("scale_by").ScaleBy(_scale, 0.45, 400)
                    .OnComplete(RefreshStatus).Play());

        Add("变换动画", "绝对旋转 Rotate", "旋转到 180°",
            """
            LA.Builder("rotate")
                .Rotate(_rotate, 180, 800)
                .Play();
            """,
            () => LA.Builder("rotate").Rotate(_rotate, 180, 800)
                    .OnComplete(RefreshStatus).Play());

        Add("变换动画", "相对旋转 RotateBy", "每次旋转 +90°",
            """
            LA.Builder("rotate_by")
                .RotateBy(_rotate, 90, 500)
                .Play();
            """,
            () => LA.Builder("rotate_by").RotateBy(_rotate, 90, 500)
                    .OnComplete(RefreshStatus).Play());

        Add("变换动画", "绝对偏斜 Skew", "AngleX / AngleY 同步偏斜到 25°",
            """
            LA.Builder("skew")
                .Skew(_skew, 25, 500)
                .Play();
            """,
            () => LA.Builder("skew").Skew(_skew, 25, 500)
                    .OnComplete(RefreshStatus).Play());

        Add("变换动画", "相对偏斜 SkewBy", "偏斜一个相对角度",
            """
            LA.Builder("skew_by")
                .SkewBy(_skew, 12, 450)
                .Play();
            """,
            () => LA.Builder("skew_by").SkewBy(_skew, 12, 450)
                    .OnComplete(RefreshStatus).Play());

        // 属性动画
        Add("属性动画", "颜色 · 元素属性", "对 Shape.FillProperty 做颜色动画",
            """
            LA.Builder("color_element")
                .Color(_swatch, Shape.FillProperty, Colors.MediumPurple, 600)
                .Play();
            """,
            () => LA.Builder("color_element")
                    .Color(_swatch, Shape.FillProperty, Colors.MediumPurple, 600)
                    .OnComplete(RefreshStatus).Play());

        Add("属性动画", "颜色 · 画刷实例", "直接动画画刷实例的颜色",
            """
            LA.Builder("color_brush")
                .Color(_cardBrush, Colors.MediumSeaGreen, 600)
                .Play();
            """,
            () => LA.Builder("color_brush")
                    .Color(_cardBrush, Colors.MediumSeaGreen, 600)
                    .OnComplete(RefreshStatus).Play());

        Add("属性动画", "不透明度 Opacity", "淡到 0.15",
            """
            LA.Builder("opacity")
                .Opacity(_opacityDot, 0.15, 450)
                .Play();
            """,
            () => LA.Builder("opacity").Opacity(_opacityDot, 0.15, 450)
                    .OnComplete(RefreshStatus).Play());

        Add("属性动画", "不透明度增量 OpacityBy", "在当前基础上增减",
            """
            LA.Builder("opacity_by")
                .OpacityBy(_opacityDot, -0.5, 450)
                .Play();
            """,
            () => LA.Builder("opacity_by").OpacityBy(_opacityDot, -0.5, 450)
                    .OnComplete(RefreshStatus).Play());

        Add("属性动画", "宽度 Width", "FrameworkElement.Width 动画",
            """
            LA.Builder("width")
                .Width(_sizeBox, 180, 450)
                .Play();
            """,
            () => LA.Builder("width").Width(_sizeBox, 180, 450)
                    .OnComplete(RefreshStatus).Play());

        Add("属性动画", "高度 Height", "FrameworkElement.Height 动画",
            """
            LA.Builder("height")
                .Height(_sizeBox, 168, 450)
                .Play();
            """,
            () => LA.Builder("height").Height(_sizeBox, 168, 450)
                    .OnComplete(RefreshStatus).Play());

        Add("属性动画", "淡入淡出 Fade", "先淡出再淡入, 1 → 0 → 1",
            """
            LA.Builder("fade")
                .FadeOut(_opacityDot, 400)
                .Then()
                .FadeIn(_opacityDot, 400)
                .Play();
            """,
            () => LA.Builder("fade")
                    .FadeOut(_opacityDot, 400)
                    .Then()
                    .FadeIn(_opacityDot, 400)
                    .OnComplete(RefreshStatus).Play());

        // 组合控制
        Add("组合控制", "序列 Then", "缩放 → 位移 → 旋转 → 变色, 依次执行",
            """
            LA.Builder("sequence")
                .Scale(_scale, 1.45, 320)
                .Then().MoveBy(_translate, 90, -40, 460)
                .Then().RotateBy(_rotate, 180, 520)
                .Then().Color(_cardBrush, Colors.Orange, 420)
                .OnComplete(...)
                .Play();
            """,
            () => LA.Builder("sequence")
                    .Scale(_scale, 1.45, 320)
                    .Then().MoveBy(_translate, 90, -40, 460)
                    .Then().RotateBy(_rotate, 180, 520)
                    .Then().Color(_cardBrush, Colors.Orange, 420)
                    .OnComplete(RefreshStatus).Play());

        Add("组合控制", "并行 (默认)", "不调用 Then 时全部动作同时开始",
            """
            LA.Builder("parallel")
                .Scale(_scale, 1.3, 700)
                .MoveBy(_translate, 70, 40, 700)
                .RotateBy(_rotate, 75, 700)
                .Color(_cardBrush, Colors.MediumSeaGreen, 700)
                .Play();
            """,
            () => LA.Builder("parallel")
                    .Scale(_scale, 1.3, 700)
                    .MoveBy(_translate, 70, 40, 700)
                    .RotateBy(_rotate, 75, 700)
                    .Color(_cardBrush, Colors.MediumSeaGreen, 700)
                    .OnComplete(RefreshStatus).Play());

        Add("组合控制", "延迟 Delay", "为下一个动作追加一次性延迟",
            """
            LA.Builder("delay")
                .Delay(700)
                .MoveBy(_translate, 150, 0, 500)
                .Play();
            """,
            () => LA.Builder("delay")
                    .Delay(700)
                    .MoveBy(_translate, 150, 0, 500)
                    .OnComplete(RefreshStatus).Play());

        Add("组合控制", "等待 Wait", "静默停顿 800ms 后再缩放",
            """
            LA.Builder("wait")
                .Wait(800)
                .Then().Scale(_scale, 1.6, 500)
                .Play();
            """,
            () => LA.Builder("wait")
                    .Wait(800)
                    .Then().Scale(_scale, 1.6, 500)
                    .OnComplete(RefreshStatus).Play());

        Add("组合控制", "回调 Callback", "序列中插入代码回调",
            """
            LA.Builder("callback")
                .Callback(() => SetStatus("回调已触发"))
                .Then().RotateBy(_rotate, 45, 400)
                .Play();
            """,
            () => LA.Builder("callback")
                    .Callback(() => SetStatus("回调已触发"))
                    .Then().RotateBy(_rotate, 45, 400)
                    .OnComplete(RefreshStatus).Play());

        Add("组合控制", "完成回调 OnComplete", "整组动画结束后触发",
            """
            LA.Builder("complete")
                .ScaleBy(_scale, 0.3, 600)
                .OnComplete(() => SetStatus("OnComplete 已触发"))
                .Play();
            """,
            () => LA.Builder("complete")
                    .ScaleBy(_scale, 0.3, 600)
                    .OnComplete(() => SetStatus("OnComplete 已触发"))
                    .Play());

        Add("组合控制", "期间/缓动默认值", "During 与 Ease 设置后续动作默认值",
            """
            LA.Builder("defaults")
                .During(900)
                .Ease(Easing.InOutCubic)
                .MoveBy(_translate, 160, 0)
                .Then()
                .MoveBy(_translate, -160, 0)
                .Play();
            """,
            () => LA.Builder("defaults")
                    .During(900)
                    .Ease(Easing.InOutCubic)
                    .MoveBy(_translate, 160, 0)
                    .Then()
                    .MoveBy(_translate, -160, 0)
                    .OnComplete(RefreshStatus).Play());

        // 缓动函数
        Add("缓动函数", "Linear 线性", "匀速, 无加减速",
            CodeEasing("Easing.Linear"),
            () => RunEasing("easing_linear", Easing.Linear));

        Add("缓动函数", "OutCubic 缓出", "引擎默认缓动",
            CodeEasing("Easing.OutCubic"),
            () => RunEasing("easing_outcubic", Easing.OutCubic));

        Add("缓动函数", "InOutCubic 缓入缓出", "两端慢, 中间快",
            CodeEasing("Easing.InOutCubic"),
            () => RunEasing("easing_inoutcubic", Easing.InOutCubic));

        Add("缓动函数", "OutPow(5) 强缓出", "指数更大的缓出",
            CodeEasing("Easing.OutPow(5)"),
            () => RunEasing("easing_outpow5", Easing.OutPow(5)));

        Add("缓动函数", "InOutPow(5) 强缓入缓出", "指数更大的缓入缓出",
            CodeEasing("Easing.InOutPow(5)"),
            () => RunEasing("easing_inoutpow5", Easing.InOutPow(5)));

        Add("缓动函数", "OutBack 回弹", "冲过终点再拉回, 适合缩放与位移",
            CodeEasing("Easing.OutBack()"),
            () => RunEasing("easing_outback", Easing.OutBack()));

        Add("缓动函数", "OutBack(2.5) 强回弹", "加大超调强度",
            CodeEasing("Easing.OutBack(2.5)"),
            () => RunEasing("easing_outback_strong", Easing.OutBack(2.5)));

        Add("缓动函数", "OutElastic 弹性", "终点附近阻尼振荡, 尾音更长",
            CodeEasing("Easing.OutElastic()"),
            () => RunEasing("easing_outelastic", Easing.OutElastic()));

        Add("缓动函数", "InOutBack 回弹进出", "起手和收尾各超调一次",
            CodeEasing("Easing.InOutBack()"),
            () => RunEasing("easing_inoutback", Easing.InOutBack()));

        // 引擎控制
        Add("引擎控制", "冻结 Freeze", "冻结后动画停止推进, 解冻后继续",
            """
            LAEngine.Freeze();     // 暂停推进
            // ... 批量设置属性 ...
            LAEngine.Unfreeze();   // 恢复
            """,
            () =>
            {
                if (LAEngine.IsFrozen) { LAEngine.Unfreeze(); SetStatus("已解冻"); }
                else { LAEngine.Freeze(); SetStatus("已冻结 (动画暂停推进)"); }
            });

        Add("引擎控制", "停止 Stop", "启动 4 秒旋转, 1 秒后按名称停止",
            """
            LA.Builder("stoppable")
                .RotateBy(_rotate, 360, 4000)
                .Play();

            LAEngine.Stop("stoppable");   // 按时长取消
            """,
            () =>
            {
                LA.Builder("stoppable").RotateBy(_rotate, 360, 4000).Play();
                _ = Task.Delay(1000).ContinueWith(_ => Dispatcher.Invoke(() =>
                {
                    LAEngine.Stop("stoppable");
                    SetStatus("已停止命名动画 'stoppable'");
                }));
            });

        Add("引擎控制", "停止全部 StopAll", "清空所有活跃动画组",
            "LAEngine.StopAll();",
            () => BtnStopAll(SetStatus));

        Add("引擎控制", "重置舞台", "复位所有变换与颜色",
            "// 复位变换、画刷与尺寸",
            () => { ResetStage(); SetStatus("舞台已重置"); });

        // 压力测试
        Add("压力测试", "颜色循环", "连续变色, 演示增量动画叠加",
            """
            for (int i = 0; i < 6; i++)
                LA.Builder($"cycle_{i}")
                    .Delay(i * 120)
                    .Color(_cardBrush, palette[i], 500)
                    .Play();
            """,
            RunColorCycle);

        Add("压力测试", "240 组并发", "压测帧驱动与分组调度性能",
            """
            for (int i = 0; i < 240; i++)
                LA.Builder($"stress_{i}")
                    .Delay(i * 4)
                    .ScaleBy(_scale, 0.004, 1400)
                    .RotateBy(_rotate, 1.2, 1400)
                    .Play();
            """,
            RunStress);
    }

    private static string CodeEasing(string easing) =>
        $$"""
        LA.Builder("easing")
            .During(900)
            .Ease({{easing}})
            .MoveBy(_translate, 170, 0)
            .Then()
            .MoveBy(_translate, -170, 0)
            .Play();
        """;

    private void RunEasing(string name, Func<double, double> easing)
    {
        LA.Builder(name)
            .During(900)
            .Ease(easing)
            .MoveBy(_translate, 170, 0)
            .Then()
            .MoveBy(_translate, -170, 0)
            .OnComplete(RefreshStatus)
            .Play();
    }

    private void RunColorCycle()
    {
        var palette = new[]
        {
            Colors.Orange, Colors.MediumSeaGreen, Colors.MediumPurple,
            Colors.Crimson, Colors.Gold, Colors.DeepSkyBlue
        };

        for (int i = 0; i < palette.Length; i++)
        {
            int index = i;
            LA.Builder($"cycle_{index}")
                .Delay(index * 130)
                .Color(_cardBrush, palette[index], 520)
                .Play();
        }
    }

    private void RunStress()
    {
        // 240 组一起推进，看帧驱动的吞吐和分组调度，同时确认帧率还稳得住。
        // 各组增量和时长完全一致，对共享变换的写入会稳定收敛，不会互相抖动。
        for (int i = 0; i < 240; i++)
        {
            LA.Builder($"stress_{i}")
                .Delay(i * 4)
                .ScaleBy(_scale, 0.0015, 1400)
                .RotateBy(_rotate, 0.45, 1400)
                .Play();
        }
        SetStatus("已启动 240 组并发动画");
    }

    private void BuildDemoList()
    {
        string? currentCategory = null;

        foreach (var demo in _demos)
        {
            if (demo.Category != currentCategory)
            {
                currentCategory = demo.Category;
                demoList.Children.Add(new TextBlock
                {
                    Text = currentCategory,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x5D, 0x68, 0x78)),
                    Margin = new Thickness(6, 14, 0, 6)
                });
            }

            var button = new Button
            {
                Content = demo.Name,
                Style = (Style)FindResource("DemoButton"),
                Tag = demo
            };
            button.Click += (_, _) => Select(demo);
            _demoButtons[demo.Name] = button;
            demoList.Children.Add(button);
        }
    }

    private void Select(Demo demo, bool autoPlay = true)
    {
        _selected = demo;

        foreach (var (name, button) in _demoButtons)
        {
            bool active = name == demo.Name;
            button.Background = active
                ? new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x4C))
                : new SolidColorBrush(Color.FromRgb(0x1C, 0x23, 0x31));
            button.BorderBrush = active
                ? (Brush)FindResource("Accent")
                : new SolidColorBrush(Color.FromRgb(0x27, 0x30, 0x41));
            button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }

        txtSelectedName.Text = demo.Name;
        txtSelectedDesc.Text = demo.Description;
        txtCode.Text = demo.Code;
        txtCodeHint.Text = demo.Category;
        txtStageTitle.Text = demo.Name;
        txtStageHint.Text = demo.Description;

        // 演示台的核心是点一下就能看到效果，所以选中即播放
        if (autoPlay)
        {
            ResetStage();
            Play(demo);
        }
    }

    private void Play(Demo demo)
    {
        _lastPlayed = demo;
        demo.Run();
        RefreshStatus();
    }

    private void BtnPlaySelected_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        ResetStage();
        Play(_selected);
    }

    private void BtnReplay_Click(object sender, RoutedEventArgs e)
    {
        var target = _selected ?? _lastPlayed;
        if (target == null) return;
        ResetStage();
        Play(target);
    }

    // 引擎控制

    private void BtnStopAll_Click(object sender, RoutedEventArgs e) => BtnStopAll(SetStatus);

    private static void BtnStopAll(Action<string> report)
    {
        LAEngine.StopAll();
        report("已停止全部动画");
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        ResetStage();
        SetStatus("舞台已重置");
    }

    private void ResetStage()
    {
        LAEngine.StopAll();

        _translate.X = 0;
        _translate.Y = 0;
        _scale.ScaleX = 1;
        _scale.ScaleY = 1;
        _rotate.Angle = 0;
        _skew.AngleX = 0;
        _skew.AngleY = 0;

        ResetBrush(_cardBrush, CardBase.Color);
        ResetBrush(_swatchBrush, SwatchBase.Color);

        _opacityDot.Opacity = 1.0;
        _sizeBox.Width = 96;
        _sizeBox.Height = 96;

        RefreshStatus();
    }

    private static void ResetBrush(SolidColorBrush brush, Color color)
    {
        // 动画可能已经把画刷换成了克隆体，这里只复位颜色，不动引用
        if (!brush.IsFrozen) brush.Color = color;
    }

    private void SliderSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // XAML 加载时会先于字段赋值触发，需要判空
        if (sliderSpeed == null || txtSpeed == null) return;
        LAEngine.Speed = sliderSpeed.Value;
        txtSpeed.Text = $"{sliderSpeed.Value:0.0}x";
    }

    // 状态与日志

    private void OnFrame(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _lastFrames);
    }

    private void RefreshStatus()
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        double elapsed = now - _lastSampleMs;
        if (elapsed >= 500)
        {
            long frames = Interlocked.Read(ref _lastFrames);
            double fps = frames * 1000.0 / elapsed;
            txtFps.Text = $"{fps:0} fps";
            _lastSampleMs = now;
            Interlocked.Exchange(ref _lastFrames, 0);
        }

        txtGroups.Text = LAEngine.ActiveGroupCount.ToString();
        txtHooked.Text = LAEngine.IsHooked ? "active" : "idle";
        txtHooked.Foreground = LAEngine.IsHooked
            ? (Brush)FindResource("Success")
            : (Brush)FindResource("TextSecondary");

        freezeBadge.Visibility = LAEngine.IsFrozen ? Visibility.Visible : Visibility.Collapsed;

        var error = LAEngine.LastError;
        txtStats.Text =
            $"组 {LAEngine.ActiveGroupCount}   ·   速度 {LAEngine.Speed:0.0}x   ·   " +
            $"冻结 {(LAEngine.IsFrozen ? "是" : "否")}   ·   " +
            (error == null ? "无异常" : $"异常 {error.GetType().Name}");
    }

    private void SetStatus(string message)
    {
        txtStatus.Text = message;
        statusDot.Fill = (Brush)FindResource("Accent");
        Log(message);
    }

    private void Log(string message)
    {
        txtLog.Text += $"{DateTime.Now:HH:mm:ss.fff}  {message}\n";
        if (txtLog.Text.Length > 12000)
            txtLog.Text = txtLog.Text[^8000..];
        logScroll.ScrollToEnd();
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e) => txtLog.Text = string.Empty;
}
