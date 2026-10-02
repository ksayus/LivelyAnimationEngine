using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LAE.DebugHost;

/// <summary>
/// 类库的 Debug 专用宿主入口，唯一目的是让 LivelyAnimationEngine.csproj 在 Debug 配置下
/// 成为可执行项目，从而能直接 F5 调试，不再提示"无法直接启动带有'类库输出类型'的项目"。
/// 该文件不参与 Release 编译，也不会进类库产物。
/// </summary>
internal static class DebugProgram
{
    [STAThread]
    internal static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Run(new DebugHostWindow());
    }
}

/// <summary>极简自检窗口，用于在类库项目内确认引擎能跑起来</summary>
internal sealed class DebugHostWindow : Window
{
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly SkewTransform _skew = new(0, 0);
    private readonly RotateTransform _rotate = new(0);
    private readonly TranslateTransform _translate = new(0, 0);
    private readonly Rectangle _rect;
    private readonly TextBlock _status;
    private DispatcherTimer? _timer;
    private SolidColorBrush _brush = null!;

    private const string GroupName = "debug_host_cycle";
    private static readonly Color BaseColor = Color.FromRgb(0x34, 0x98, 0xDB);

    public DebugHostWindow()
    {
        Title = "LAE 类库调试宿主";
        Width = 720;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1F, 0x2B));
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 标题
        var header = new StackPanel();
        header.Children.Add(new TextBlock
        {
            Text = "LAE — Lively Animation Engine",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5D, 0xD3, 0xE8))
        });
        header.Children.Add(new TextBlock
        {
            Text = $"类库调试宿主 · v{Assembly.GetExecutingAssembly().GetName().Version} · 已直接启动成功",
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA5, 0xB1))
        });
        header.Children.Add(new TextBlock
        {
            Text = "此窗口用于在类库项目内直接 F5 调试。完整的引擎演示界面请启动 UI 项目。",
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x85))
        });
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // 状态栏
        _status = new TextBlock
        {
            FontSize = 12,
            Margin = new Thickness(0, 12, 0, 0),
            FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0xE7, 0x87))
        };
        Grid.SetRow(_status, 1);
        root.Children.Add(_status);

        // 演示画布
        var canvas = new Canvas
        {
            Background = new SolidColorBrush(Color.FromRgb(0x11, 0x14, 0x1C)),
            ClipToBounds = true,
            Margin = new Thickness(0, 12, 0, 12)
        };
        Grid.SetRow(canvas, 2);

        var transformGroup = new TransformGroup();
        transformGroup.Children.Add(_scale);
        transformGroup.Children.Add(_skew);
        transformGroup.Children.Add(_rotate);
        transformGroup.Children.Add(_translate);

        _rect = new Rectangle
        {
            Width = 88,
            Height = 88,
            RadiusX = 14,
            RadiusY = 14,
            Fill = new SolidColorBrush(BaseColor),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = transformGroup
        };
        _brush = (SolidColorBrush)_rect.Fill;
        Canvas.SetLeft(_rect, 150);
        Canvas.SetTop(_rect, 90);
        canvas.Children.Add(_rect);

        var caption = new TextBlock
        {
            Text = "引擎自检: 缩放 · 偏斜 · 旋转 · 位移 · 变色",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x85))
        };
        Canvas.SetLeft(caption, 12);
        Canvas.SetTop(caption, 12);
        canvas.Children.Add(caption);
        root.Children.Add(canvas);

        // 按钮
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(MakeButton("播放一轮动画 (Enter)", Brushes.White,
            new SolidColorBrush(Color.FromRgb(0x2D, 0x7D, 0xD2)), PlayCycle));
        buttons.Children.Add(MakeButton("停止 (Esc)", Brushes.White,
            new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B)), StopAll));
        buttons.Children.Add(MakeButton("重置", Brushes.White,
            new SolidColorBrush(Color.FromRgb(0x44, 0x4C, 0x5A)), Reset));
        buttons.Children.Add(MakeButton("关闭", Brushes.White,
            new SolidColorBrush(Color.FromRgb(0x44, 0x4C, 0x5A)), Close));
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        Content = root;

        InputBindings.Add(new System.Windows.Input.KeyBinding(
            new RelayCommand(PlayCycle), System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.None));
        InputBindings.Add(new System.Windows.Input.KeyBinding(
            new RelayCommand(StopAll), System.Windows.Input.Key.Escape, System.Windows.Input.ModifierKeys.None));

        Loaded += (_, _) =>
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _timer.Tick += (_, _) => RefreshStatus();
            _timer.Start();
            RefreshStatus();
            PlayCycle();
        };
        Closed += (_, _) => _timer?.Stop();
    }

    private static Button MakeButton(string text, Brush fore, Brush back, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            Foreground = fore,
            Background = back,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 0, 10, 0),
            FontSize = 13,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private void RefreshStatus()
    {
        _status.Text =
            $"活跃组 {LAE.LAEngine.ActiveGroupCount}   |   冻结 {LAE.LAEngine.IsFrozen}   |   " +
            $"速度 {LAE.LAEngine.Speed:0.0}x   |   引擎订阅中 {LAE.LAEngine.IsHooked}";
    }

    private void PlayCycle()
    {
        Reset();

        LA.Builder(GroupName)
            .During(600)
            .Ease(Easing.OutCubic)
            .Scale(_scale, 1.6)
            .RotateBy(_rotate, 180)
            .SkewBy(_skew, 8)
            .MoveBy(_translate, 180, 0)
            .Then()
            .During(500)
            .ScaleBy(_scale, -0.35)
            .MoveBy(_translate, -180, 0)
            .RotateBy(_rotate, -180)
            .Then()
            .During(700)
            .Color(_rect, Shape.FillProperty, Colors.MediumPurple)
            .OnComplete(RefreshStatus)
            .Play();

        RefreshStatus();
    }

    private void StopAll()
    {
        LAEngine.StopAll();
        RefreshStatus();
    }

    private void Reset()
    {
        LAEngine.Stop(GroupName);
        _scale.ScaleX = 1;
        _scale.ScaleY = 1;
        _skew.AngleX = 0;
        _skew.AngleY = 0;
        _rotate.Angle = 0;
        _translate.X = 0;
        _translate.Y = 0;

        // 颜色动画可能已经把 Fill 换成了可写克隆体，统一按实例引用复位
        if (_brush.IsFrozen)
        {
            _brush = _brush.CloneCurrentValue();
            _rect.Fill = _brush;
        }
        _brush.Color = BaseColor;
    }

    /// <summary>最简 ICommand，只给按键绑定用</summary>
    private sealed class RelayCommand : System.Windows.Input.ICommand
    {
        private readonly Action _action;
        public RelayCommand(Action action) => _action = action;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _action();
    }
}
