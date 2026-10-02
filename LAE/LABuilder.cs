using System.Windows;
using System.Windows.Media;

namespace LAE;

public static class LA
{
    /// <summary>创建命名动画构建器；同名动画注册时会自动停掉旧的那组</summary>
    public static LABuilder Builder(string name) => new LABuilder(name);

    /// <summary>创建匿名动画构建器（注册时分配唯一名字）</summary>
    public static LABuilder Builder() => new LABuilder(null);
}

/// <summary>
/// 流式动画构建器，逐条累计动作。
/// 默认所有动作并行开始，调用 <see cref="Then"/> 之后的动作要等前序全部完成。
/// </summary>
public sealed class LABuilder
{
    private readonly LAGroup _group;

    private double _defaultDuration = 300;
    private Func<double, double> _defaultEasing = Easing.OutCubic;
    private double _pendingDelay;       // 只作用于下一个动作，用完清零
    private bool _sequenceBarrier;      // 下一个动作是否等待前序
    private int _step = -1;             // 步骤号，一个动作或一组组合动作占一个号

    internal LABuilder(string? name)
    {
        _group = new LAGroup();
        if (!string.IsNullOrEmpty(name))
            _group.Name = name;
    }


    // 默认值，作用于之后加入的所有动作

    /// <summary>设置后续动作的默认时长（毫秒）</summary>
    public LABuilder During(double milliseconds)
    {
        _defaultDuration = milliseconds;
        return this;
    }

    /// <summary>设置后续动作的默认缓动，默认 <see cref="Easing.OutCubic"/></summary>
    public LABuilder Ease(Func<double, double> easing)
    {
        _defaultEasing = easing ?? Easing.Linear;
        return this;
    }


    // 序列与延迟

    /// <summary>
    /// 插入序列分隔：之后的动作等待此前所有动作完成才开始。
    /// 同一个分隔点之后加入的多个动作彼此仍是并行。
    /// </summary>
    public LABuilder Then()
    {
        _sequenceBarrier = true;
        return this;
    }

    /// <summary>给下一个动作加一次性延迟（毫秒）</summary>
    public LABuilder Delay(double milliseconds)
    {
        _pendingDelay += milliseconds;
        return this;
    }

    /// <summary>
    /// 插入一段静止停顿。Wait 自身就是序列分隔点：先等前序完成，再吃掉指定时长，
    /// 之后的动作才开始。
    /// </summary>
    public LABuilder Wait(double milliseconds)
    {
        // 不论之前有没有调过 Then()，Wait 都要等前序；它自己也是分隔点
        bool barrier = _sequenceBarrier;
        _sequenceBarrier = false;
        _step++;
        _group.AddAction(new WaitLA(milliseconds, true), barrier, _step);
        _sequenceBarrier = true;
        return this;
    }


    // 依赖属性数值动画

    /// <summary>把依赖属性动画到绝对目标值</summary>
    public LABuilder To(
        DependencyObject target, DependencyProperty property, double endValue,
        double? duration = null, Func<double, double>? easing = null)
        => ToCore(target, property, endValue, false, duration, easing,
            double.NegativeInfinity, double.PositiveInfinity);

    /// <summary>把依赖属性动画一个相对增量</summary>
    public LABuilder By(
        DependencyObject target, DependencyProperty property, double deltaValue,
        double? duration = null, Func<double, double>? easing = null)
        => ToCore(target, property, deltaValue, true, duration, easing,
            double.NegativeInfinity, double.PositiveInfinity);

    /// <summary>
    /// To / By 的公共实现。minValue / maxValue 是结果边界：回弹、弹性曲线会把值推出
    /// [起点, 终点]，取值范围有硬要求的属性就靠这两个边界兜住。
    /// </summary>
    private LABuilder ToCore(
        DependencyObject target, DependencyProperty property, double value, bool relative,
        double? duration, Func<double, double>? easing, double minValue, double maxValue)
    {
        AddStep(new DependencyPropertyLA(target, property, value, relative,
            duration ?? _defaultDuration, ConsumeDelay(),
            easing ?? _defaultEasing, false, minValue, maxValue));
        return this;
    }


    // ScaleTransform（X/Y 同步）

    /// <summary>把 ScaleTransform 缩放到绝对倍率</summary>
    public LABuilder Scale(
        ScaleTransform transform, double endScale,
        double? duration = null, Func<double, double>? easing = null)
    {
        double dur = duration ?? _defaultDuration;
        Func<double, double> ez = easing ?? _defaultEasing;
        double delay = ConsumeDelay();

        // ScaleX / ScaleY 各自成一条动画，但共用同一步骤，所以一起等门一起起步
        AddPair(
            new DependencyPropertyLA(transform, ScaleTransform.ScaleXProperty,
                endScale, false, dur, delay, ez, false, 0),
            new DependencyPropertyLA(transform, ScaleTransform.ScaleYProperty,
                endScale, false, dur, delay, ez, false, 0));
        return this;
    }

    /// <summary>把 ScaleTransform 缩放一个相对增量</summary>
    public LABuilder ScaleBy(
        ScaleTransform transform, double deltaScale,
        double? duration = null, Func<double, double>? easing = null)
    {
        double dur = duration ?? _defaultDuration;
        Func<double, double> ez = easing ?? _defaultEasing;
        double delay = ConsumeDelay();

        AddPair(
            new DependencyPropertyLA(transform, ScaleTransform.ScaleXProperty,
                deltaScale, true, dur, delay, ez, false, 0),
            new DependencyPropertyLA(transform, ScaleTransform.ScaleYProperty,
                deltaScale, true, dur, delay, ez, false, 0));
        return this;
    }


    // RotateTransform

    /// <summary>把 RotateTransform 旋转到绝对角度（度）</summary>
    public LABuilder Rotate(
        RotateTransform transform, double endAngle,
        double? duration = null, Func<double, double>? easing = null)
    {
        AddStep(new RotateTransformLA(transform, endAngle, false,
            duration ?? _defaultDuration, ConsumeDelay(),
            easing ?? _defaultEasing, false));
        return this;
    }

    /// <summary>把 RotateTransform 旋转一个相对角度增量（度）</summary>
    public LABuilder RotateBy(
        RotateTransform transform, double deltaAngle,
        double? duration = null, Func<double, double>? easing = null)
    {
        AddStep(new RotateTransformLA(transform, deltaAngle, true,
            duration ?? _defaultDuration, ConsumeDelay(),
            easing ?? _defaultEasing, false));
        return this;
    }


    // TranslateTransform（X/Y 联动）

    /// <summary>把 TranslateTransform 移动到绝对坐标</summary>
    public LABuilder Move(
        TranslateTransform transform, double endX, double endY,
        double? duration = null, Func<double, double>? easing = null)
    {
        double dur = duration ?? _defaultDuration;
        Func<double, double> ez = easing ?? _defaultEasing;
        double delay = ConsumeDelay();

        // X/Y 同属一步：一起等门、一起起步，彼此不算对方的前序
        AddPair(
            new DependencyPropertyLA(transform, TranslateTransform.XProperty,
                endX, false, dur, delay, ez, false),
            new DependencyPropertyLA(transform, TranslateTransform.YProperty,
                endY, false, dur, delay, ez, false));
        return this;
    }

    /// <summary>把 TranslateTransform 移动一个相对增量</summary>
    public LABuilder MoveBy(
        TranslateTransform transform, double deltaX, double deltaY,
        double? duration = null, Func<double, double>? easing = null)
    {
        double dur = duration ?? _defaultDuration;
        Func<double, double> ez = easing ?? _defaultEasing;
        double delay = ConsumeDelay();

        AddPair(
            new DependencyPropertyLA(transform, TranslateTransform.XProperty,
                deltaX, true, dur, delay, ez, false),
            new DependencyPropertyLA(transform, TranslateTransform.YProperty,
                deltaY, true, dur, delay, ez, false));
        return this;
    }


    // 颜色

    /// <summary>把画刷实例的颜色动画到目标色（sRGB 四通道线性插值）</summary>
    public LABuilder Color(
        SolidColorBrush brush, Color endColor,
        double? duration = null, Func<double, double>? easing = null)
    {
        AddStep(new ColorLA(brush, endColor,
                duration ?? _defaultDuration, ConsumeDelay(),
                easing ?? _defaultEasing, false));
        return this;
    }

    /// <summary>
    /// 把元素上的画刷属性动画到目标色，冻结的画刷会自动克隆后回设。
    /// </summary>
    /// <param name="property">画刷依赖属性，例如 <c>Shape.FillProperty</c></param>
    public LABuilder Color(
        DependencyObject target, DependencyProperty property, Color endColor,
        double? duration = null, Func<double, double>? easing = null)
    {
        // 属性类型在构建期就校验，早点报错；
        // 冻结画刷的克隆和回设推迟到动画真正启动时再做。
        if (target.GetValue(property) is not SolidColorBrush)
            throw new InvalidOperationException($"Property {property.Name} is not a SolidColorBrush");

        AddStep(new ColorLA(target, property, endColor,
                duration ?? _defaultDuration, ConsumeDelay(),
                easing ?? _defaultEasing, false));
        return this;
    }


    // 回调

    /// <summary>插入一段代码回调，时长为 0，可以带延迟</summary>
    public LABuilder Callback(Action action)
    {
        AddStep(new CallbackLA(action, ConsumeDelay(), false));
        return this;
    }


    // SkewTransform

    /// <summary>把 SkewTransform 偏斜到绝对角度（度）</summary>
    public LABuilder Skew(
        SkewTransform transform, double endAngle,
        double? duration = null, Func<double, double>? easing = null)
    {
        double dur = duration ?? _defaultDuration;
        Func<double, double> ez = easing ?? _defaultEasing;
        double delay = ConsumeDelay();

        // AngleX / AngleY 同步，且同属一步
        AddPair(
            new DependencyPropertyLA(transform, SkewTransform.AngleXProperty,
                endAngle, false, dur, delay, ez, false),
            new DependencyPropertyLA(transform, SkewTransform.AngleYProperty,
                endAngle, false, dur, delay, ez, false));
        return this;
    }

    /// <summary>把 SkewTransform 偏斜一个相对角度增量（度）</summary>
    public LABuilder SkewBy(
        SkewTransform transform, double deltaAngle,
        double? duration = null, Func<double, double>? easing = null)
    {
        double dur = duration ?? _defaultDuration;
        Func<double, double> ez = easing ?? _defaultEasing;
        double delay = ConsumeDelay();

        AddPair(
            new DependencyPropertyLA(transform, SkewTransform.AngleXProperty,
                deltaAngle, true, dur, delay, ez, false),
            new DependencyPropertyLA(transform, SkewTransform.AngleYProperty,
                deltaAngle, true, dur, delay, ez, false));
        return this;
    }


    // 常用属性快捷方法。这几个属性有硬性取值范围，所以直接带上结果边界，
    // 免得超调曲线把不透明度推到 1 以上、或者把宽高推成负数让 WPF 抛异常。

    /// <summary>把元素不透明度动画到目标值（0~1）</summary>
    public LABuilder Opacity(
        UIElement target, double endValue,
        double? duration = null, Func<double, double>? easing = null)
        => ToCore(target, UIElement.OpacityProperty, endValue, false, duration, easing, 0.0, 1.0);

    /// <summary>把元素不透明度动画一个相对增量</summary>
    public LABuilder OpacityBy(
        UIElement target, double deltaValue,
        double? duration = null, Func<double, double>? easing = null)
        => ToCore(target, UIElement.OpacityProperty, deltaValue, true, duration, easing, 0.0, 1.0);

    /// <summary>把元素宽度动画到目标值，结果不小于 0</summary>
    public LABuilder Width(
        FrameworkElement target, double endValue,
        double? duration = null, Func<double, double>? easing = null)
        => ToCore(target, FrameworkElement.WidthProperty, endValue, false, duration, easing,
            0.0, double.PositiveInfinity);

    /// <summary>把元素宽度动画一个相对增量，结果不小于 0</summary>
    public LABuilder WidthBy(
        FrameworkElement target, double deltaValue,
        double? duration = null, Func<double, double>? easing = null)
        => ToCore(target, FrameworkElement.WidthProperty, deltaValue, true, duration, easing,
            0.0, double.PositiveInfinity);

    /// <summary>把元素高度动画到目标值，结果不小于 0</summary>
    public LABuilder Height(
        FrameworkElement target, double endValue,
        double? duration = null, Func<double, double>? easing = null)
        => ToCore(target, FrameworkElement.HeightProperty, endValue, false, duration, easing,
            0.0, double.PositiveInfinity);

    /// <summary>把元素高度动画一个相对增量，结果不小于 0</summary>
    public LABuilder HeightBy(
        FrameworkElement target, double deltaValue,
        double? duration = null, Func<double, double>? easing = null)
        => ToCore(target, FrameworkElement.HeightProperty, deltaValue, true, duration, easing,
            0.0, double.PositiveInfinity);

    /// <summary>淡入：不透明度从当前值动画到 1</summary>
    public LABuilder FadeIn(
        UIElement target,
        double? duration = null, Func<double, double>? easing = null)
    {
        return Opacity(target, 1.0, duration, easing);
    }

    /// <summary>淡出：不透明度从当前值动画到 0</summary>
    public LABuilder FadeOut(
        UIElement target,
        double? duration = null, Func<double, double>? easing = null)
    {
        return Opacity(target, 0.0, duration, easing);
    }


    // 收尾

    /// <summary>设置整组动画完成后的回调</summary>
    public LABuilder OnComplete(Action callback)
    {
        _group.OnComplete = callback;
        return this;
    }

    /// <summary>取出动画组，不启动</summary>
    public LAGroup BuildGroup() => _group;

    /// <summary>注册并启动动画，返回组名，后续可以用它 Stop</summary>
    public string Play()
    {
        LAEngine.Play(_group);
        return _group.Name;
    }


    // 内部工具

    /// <summary>加入一个普通动作：消费序列门，并给它分配新的步骤号</summary>
    private void AddStep(ILAAction action)
    {
        bool barrier = _sequenceBarrier;
        _sequenceBarrier = false;
        _step++;
        _group.AddAction(action, barrier, _step);
    }

    /// <summary>
    /// 加入由两个子动作组成的组合动作（如 Move 的 X/Y）。
    /// 两者共用步骤号和序列门：一起等前序、一起开始，且彼此互不算前序，
    /// 否则后一个会被前一个卡住。
    /// </summary>
    private void AddPair(ILAAction first, ILAAction second)
    {
        bool barrier = _sequenceBarrier;
        _sequenceBarrier = false;
        _step++;
        _group.AddAction(first, barrier, _step);
        _group.AddAction(second, barrier, _step);
    }

    /// <summary>取出并清除一次性延迟</summary>
    private double ConsumeDelay()
    {
        double d = _pendingDelay;
        _pendingDelay = 0;
        return d;
    }
}
