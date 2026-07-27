using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LAE;

namespace UI;

public partial class MainWindow : Window
{
    private readonly SolidColorBrush _brushMain;
    private readonly SolidColorBrush _brushColor;
    private bool _moved;

    public MainWindow()
    {
        InitializeComponent();
        _brushMain = (SolidColorBrush)rectMain.Fill;
        _brushColor = (SolidColorBrush)rectColor.Fill;

        // 定时刷新状态栏
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        timer.Tick += (_, _) => RefreshStatus();
        timer.Start();

        Log("LAE 引擎全功能测试界面已就绪");
        Log("共 38 个测试按钮，覆盖所有引擎功能");
    }

    // ── 日志 ──
    private void Log(string msg)
    {
        txtLog.Text += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
        logScroll.ScrollToEnd();
    }

    private void RefreshStatus()
    {
        txtActiveGroups.Text = $"活跃组: {LAEngine.ActiveGroupCount}";
        txtFrozen.Text = $"冻结: {(LAEngine.IsFrozen ? "是" : "否")}";
        txtSpeedInfo.Text = $"速度: {LAEngine.Speed:F1}x";
    }

    private void SetStatus(string msg)
    {
        txtStatus.Text = msg;
        Log(msg);
    }

    // ── 重置 ──
    private void BtnResetAll_Click(object sender, RoutedEventArgs e)
    {
        LAEngine.StopAll();
        translateTransform.X = 0;
        translateTransform.Y = 0;
        scaleTransform.ScaleX = 1;
        scaleTransform.ScaleY = 1;
        rotateTransform.Angle = 0;
        skewTransform.AngleX = 0;
        skewTransform.AngleY = 0;
        _brushMain.Color = Color.FromRgb(0x34, 0x98, 0xDB);
        _brushColor.Color = Color.FromRgb(0xE7, 0x4C, 0x3C);
        ellipseOpacity.Opacity = 1.0;
        borderSize.Width = 80;
        borderSize.Height = 80;
        rectFade.Opacity = 1.0;
        _moved = false;
        SetStatus("全部重置");
    }

    // ── 变换动画 ──

    private void BtnMoveBy_Click(object sender, RoutedEventArgs e)
    {
        double dx = _moved ? -150 : 150;
        _moved = !_moved;
        LA.Builder().MoveBy(translateTransform, dx, 0, 600).Play();
        SetStatus($"MoveBy: X +{dx}, Y +0");
    }

    private void BtnMoveTo_Click(object sender, RoutedEventArgs e)
    {
        LA.Builder().Move(translateTransform, 100, -50, 500).Play();
        SetStatus("MoveTo: X=100, Y=-50");
    }

    private void BtnScale_Click(object sender, RoutedEventArgs e)
    {
        LA.Builder().Scale(scaleTransform, 2.0, 400).Play();
        SetStatus("Scale: to 2.0x");
    }

    private void BtnScaleBy_Click(object sender, RoutedEventArgs e)
    {
        double ds = scaleTransform.ScaleX > 1.5 ? -0.5 : 0.5;
        LA.Builder().ScaleBy(scaleTransform, ds, 400).Play();
        SetStatus($"ScaleBy: {ds:+0.0;-0.0}");
    }

    private void BtnRotate_Click(object sender, RoutedEventArgs e)
    {
        LA.Builder().Rotate(rotateTransform, 180, 700).Play();
        SetStatus("Rotate: to 180°");
    }

    private void BtnRotateBy_Click(object sender, RoutedEventArgs e)
    {
        LA.Builder().RotateBy(rotateTransform, 90, 500).Play();
        SetStatus("RotateBy: +90°");
    }

    private void BtnSkew_Click(object sender, RoutedEventArgs e)
    {
        LA.Builder().Skew(skewTransform, 30, 500).Play();
        SetStatus("Skew: to 30°");
    }

    private void BtnSkewBy_Click(object sender, RoutedEventArgs e)
    {
        double ds = skewTransform.AngleX > 15 ? -20 : 20;
        LA.Builder().SkewBy(skewTransform, ds, 500).Play();
        SetStatus($"SkewBy: {ds:+0;-0}°");
    }

    // ── 属性动画 ──

    private void BtnColor_Click(object sender, RoutedEventArgs e)
    {
        var colors = new[] { Colors.Red, Colors.Green, Colors.Blue, Colors.Orange, Colors.Purple };
        var next = colors[Random.Shared.Next(colors.Length)];
        LA.Builder().Color(_brushMain, next, 500).Play();
        SetStatus($"Color (brush): {next}");
    }

    private void BtnColorElement_Click(object sender, RoutedEventArgs e)
    {
        var colors = new[] { Colors.Yellow, Colors.Cyan, Colors.Magenta, Colors.White, Colors.Lime };
        var next = colors[Random.Shared.Next(colors.Length)];
        LA.Builder().Color(rectColor, Shape.FillProperty, next, 500).Play();
        SetStatus($"Color (element): {next}");
    }

    private void BtnOpacity_Click(object sender, RoutedEventArgs e)
    {
        double target = ellipseOpacity.Opacity > 0.5 ? 0.2 : 1.0;
        LA.Builder().Opacity(ellipseOpacity, target, 400).Play();
        SetStatus($"Opacity: to {target:F1}");
    }

    private void BtnOpacityBy_Click(object sender, RoutedEventArgs e)
    {
        double delta = ellipseOpacity.Opacity > 0.5 ? -0.4 : 0.4;
        LA.Builder().OpacityBy(ellipseOpacity, delta, 400).Play();
        SetStatus($"OpacityBy: {delta:+0.0;-0.0}");
    }

    private void BtnWidth_Click(object sender, RoutedEventArgs e)
    {
        double target = borderSize.Width > 100 ? 80 : 150;
        LA.Builder().Width(borderSize, target, 400).Play();
        SetStatus($"Width: to {target}");
    }

    private void BtnWidthBy_Click(object sender, RoutedEventArgs e)
    {
        LA.Builder().WidthBy(borderSize, 50, 400).Play();
        SetStatus("WidthBy: +50");
    }

    private void BtnHeight_Click(object sender, RoutedEventArgs e)
    {
        double target = borderSize.Height > 100 ? 80 : 150;
        LA.Builder().Height(borderSize, target, 400).Play();
        SetStatus($"Height: to {target}");
    }

    private void BtnHeightBy_Click(object sender, RoutedEventArgs e)
    {
        LA.Builder().HeightBy(borderSize, 50, 400).Play();
        SetStatus("HeightBy: +50");
    }

    // ── 快捷方法 ──

    private void BtnFadeIn_Click(object sender, RoutedEventArgs e)
    {
        rectFade.Opacity = 0.0;
        LA.Builder().FadeIn(rectFade, 500).OnComplete(() => SetStatus("FadeIn 完成")).Play();
        SetStatus("FadeIn: 0 -> 1");
    }

    private void BtnFadeOut_Click(object sender, RoutedEventArgs e)
    {
        rectFade.Opacity = 1.0;
        LA.Builder().FadeOut(rectFade, 500).OnComplete(() => SetStatus("FadeOut 完成")).Play();
        SetStatus("FadeOut: 1 -> 0");
    }

    // ── 组合控制 ──

    private void BtnSeq_Click(object sender, RoutedEventArgs e)
    {
        LA.Builder("seq_test")
            .Scale(scaleTransform, 1.5, 300)
            .Then()
            .MoveBy(translateTransform, 80, 0, 500)
            .Then()
            .RotateBy(rotateTransform, 180, 500)
            .Then()
            .Color(_brushMain, Colors.Orange, 400)
            .OnComplete(() => SetStatus("序列动画完成"))
            .Play();
        SetStatus("Sequence: 缩放→移动→旋转→变色");
    }

    private void BtnPar_Click(object sender, RoutedEventArgs e)
    {
        LA.Builder("par_test")
            .Scale(scaleTransform, 1.3, 600)
            .MoveBy(translateTransform, 60, 30, 600)
            .RotateBy(rotateTransform, 60, 600)
            .Color(_brushMain, Colors.Green, 600)
            .OnComplete(() => SetStatus("并行动画完成"))
            .Play();
        SetStatus("Parallel: 同时 缩放+移动+旋转+变色");
    }

    private void BtnDelay_Click(object sender, RoutedEventArgs e)
    {
        LA.Builder("delay_test")
            .Delay(800)
            .MoveBy(translateTransform, 120, 0, 500)
            .OnComplete(() => SetStatus("延迟动画完成"))
            .Play();
        SetStatus("Delay: 800ms 延迟后移动");
    }

    private void BtnWait_Click(object sender, RoutedEventArgs e)
    {
        LA.Builder("wait_test")
            .Wait(1000)
            .OnComplete(() => SetStatus("Wait 1s 完成"))
            .Play();
        SetStatus("Wait: 等待 1 秒");
    }

    private void BtnCallback_Click(object sender, RoutedEventArgs e)
    {
        bool callbackFired = false;
        LA.Builder("callback_test")
            .Callback(() =>
            {
                callbackFired = true;
                SetStatus("Callback 已触发!");
            })
            .Then()
            .RotateBy(rotateTransform, 45, 300)
            .OnComplete(() =>
            {
                if (callbackFired)
                    SetStatus("Callback + Rotate 序列完成 ✓");
            })
            .Play();
        SetStatus("Callback: 先回调, 再旋转");
    }

    private void BtnOnComplete_Click(object sender, RoutedEventArgs e)
    {
        int count = 0;
        LA.Builder("complete_test")
            .ScaleBy(scaleTransform, 0.3, 400)
            .OnComplete(() =>
            {
                count++;
                SetStatus($"OnComplete 触发 (第{count}次)");
            })
            .Play();
        SetStatus("OnComplete: 缩放后回调");
    }

    // ── 引擎控制 ──

    private void BtnStopNamed_Click(object sender, RoutedEventArgs e)
    {
        // 先启动一个命名动画
        LA.Builder("stoppable")
            .RotateBy(rotateTransform, 360, 3000)
            .OnComplete(() => SetStatus("stoppable 正常完成"))
            .Play();
        SetStatus("启动命名动画 'stoppable' (3s)");

        // 1.5秒后停止
        _ = Task.Delay(1500).ContinueWith(_ =>
        {
            Dispatcher.Invoke(() =>
            {
                LAEngine.Stop("stoppable");
                SetStatus("Stop: 'stoppable' 已停止");
            });
        });
    }

    private void BtnIsRunning_Click(object sender, RoutedEventArgs e)
    {
        bool running = LAEngine.IsRunning("stoppable");
        SetStatus($"IsRunning('stoppable'): {running}");

        // 也检查一个不存在的
        bool notRunning = LAEngine.IsRunning("nonexistent");
        Log($"IsRunning('nonexistent'): {notRunning}");
    }

    private void BtnActiveCount_Click(object sender, RoutedEventArgs e)
    {
        SetStatus($"ActiveGroupCount: {LAEngine.ActiveGroupCount}");
    }

    private void BtnIsFrozen_Click(object sender, RoutedEventArgs e)
    {
        SetStatus($"IsFrozen: {LAEngine.IsFrozen}");
    }

    // ── 缓动函数 ──

    private void BtnEasingLinear_Click(object sender, RoutedEventArgs e)
    {
        LA.Builder("easing_test")
            .Ease(Easing.Linear)
            .MoveBy(translateTransform, 100, 0, 800)
            .OnComplete(() => SetStatus("Linear 完成"))
            .Play();
        SetStatus("Easing: Linear (线性)");
    }

    private void BtnEasingOutCubic_Click(object sender, RoutedEventArgs e)
    {
        LA.Builder("easing_test")
            .Ease(Easing.OutCubic)
            .MoveBy(translateTransform, 100, 0, 800)
            .OnComplete(() => SetStatus("OutCubic 完成"))
            .Play();
        SetStatus("Easing: OutCubic (缓出)");
    }

    private void BtnEasingInOutCubic_Click(object sender, RoutedEventArgs e)
    {
        LA.Builder("easing_test")
            .Ease(Easing.InOutCubic)
            .MoveBy(translateTransform, 100, 0, 800)
            .OnComplete(() => SetStatus("InOutCubic 完成"))
            .Play();
        SetStatus("Easing: InOutCubic (缓入缓出)");
    }

    private void BtnEasingOutPow5_Click(object sender, RoutedEventArgs e)
    {
        LA.Builder("easing_test")
            .Ease(Easing.OutPow(5))
            .MoveBy(translateTransform, 100, 0, 800)
            .OnComplete(() => SetStatus("OutPow(5) 完成"))
            .Play();
        SetStatus("Easing: OutPow(5) (强缓出)");
    }

    private void BtnEasingInOutPow5_Click(object sender, RoutedEventArgs e)
    {
        LA.Builder("easing_test")
            .Ease(Easing.InOutPow(5))
            .MoveBy(translateTransform, 100, 0, 800)
            .OnComplete(() => SetStatus("InOutPow(5) 完成"))
            .Play();
        SetStatus("Easing: InOutPow(5) (强缓入缓出)");
    }

    // ── 冻结/解冻/停止/速度 ──

    private void BtnFreeze_Click(object sender, RoutedEventArgs e)
    {
        LAEngine.Freeze();
        SetStatus($"冻结: _freezeCounter={LAEngine.IsFrozen}");
    }

    private void BtnUnfreeze_Click(object sender, RoutedEventArgs e)
    {
        LAEngine.Unfreeze();
        SetStatus($"解冻: _freezeCounter={LAEngine.IsFrozen}");
    }

    private void BtnStopAll_Click(object sender, RoutedEventArgs e)
    {
        LAEngine.StopAll();
        SetStatus("StopAll: 所有动画已停止");
    }

    private void SliderSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // XAML 加载期间 Slider 的 Minimum/Maximum/Value 设置会触发此事件,
        // 此时 sliderSpeed 和 txtSpeed 尚未被 XAML 加载器赋值, 需判空
        if (sliderSpeed == null || txtSpeed == null) return;
        LAEngine.Speed = sliderSpeed.Value;
        txtSpeed.Text = $"{sliderSpeed.Value:F1}x";
    }
}