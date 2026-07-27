using System.Windows;
using System.Windows.Media;

namespace LAE;

public static class LA
{
    /// <summary>
    /// 创建一个有名字动画构建器, 同名动画注册时自动停止旧的
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public static LABuilder Builder(string name) => new LABuilder(name);
    /// <summary>
    /// 创建一个匿名动画构建器(每次注册分配唯一名)
    /// </summary>
    /// <returns></returns>
    public static LABuilder Builder() => new LABuilder(null);
}

/// <summary>
/// 流式动画构建器,累计动画动作,支持并行(默认)与序列(Then)两种组合方式
/// 默认动作并执行,调用Then()后,后续动作等待前序全部完成再开始
/// </summary>
public sealed class LABuilder
{
    private readonly LAGroup _group;

    private double _defaultDuration = 300;
    private Func<double, double> _defaultEasing = Easing.OutCubic;
    private double _pendingDelay;       // 仅用于下一个动作(一次性)
    private bool _sequenceBarrier;      // 下一个动作是否等待前序

    internal LABuilder(string? name)
    {
        _group = new LAGroup();
        if (!string.IsNullOrEmpty(name))
            _group.Name = name;
    }


    // 默认值设置(影响后续所有动作,直到再次修改)

    // 设置后续动作的默认时长
    public LABuilder During(double milliseconds)
    {
        _defaultDuration = milliseconds;
        return this;
    }

    // 设置后续动作的默认缓动函数 默认 OutCubic
    public LABuilder Ease(Func<double, double> easing)
    {
        _defaultEasing = easing ?? Easing.Linear;
        return this;
    }


    // 序列与延迟控制

    /// <summary>
    /// 插入序列分隔: 调用后加入的动作会等待之前所有动作完成再开始
    /// 同一分隔后加入的多个动作彼此并行
    /// </summary>
    /// <returns></returns>
    public LABuilder Then()
    {
        _sequenceBarrier = true;
        return this;
    }
    /// <summary>
    /// 为下一个动作追加延迟(毫秒,一次性,用后即止)
    /// </summary>
    /// <param name="milliseconds"></param>
    /// <returns></returns>
    public LABuilder Delay(double milliseconds)
    {
        _pendingDelay += milliseconds;
        return this;
    }
    /// <summary>
    /// 在序列中插入一段静止停顿   Wait 始终作为序列分隔点:
    /// 先等待前序全部完成,再静默消耗指定时长,之后后续动作才开始
    /// </summary>
    /// <param name="milliseconds"></param>
    /// <returns></returns>
    public LABuilder Wait(double milliseconds)
    {
        // 始终等待前序,无论是否调用Then()
        Add(new WaitLA(milliseconds, true));
        _sequenceBarrier = true;
        return this;
    }


    // 数值属性动画

    /// <summary>
    /// 将依赖属性动画到绝对目标值
    /// </summary>
    /// <param name="target"></param>
    /// <param name="property"></param>
    /// <param name="endValue"></param>
    /// <param name="duration"></param>
    /// <param name="easing"></param>
    /// <returns></returns>
    public LABuilder To(
        DependencyObject target, DependencyProperty property,double endValue,
        double? duration = null, Func<double, double>? easing = null)
    {
        Add(new DependencyPropertyLA(target, property, endValue, false,
            duration ?? _defaultDuration, ConsumeDelay(),
            easing ?? _defaultEasing, ConsumeBarrier()));
        return this;
    }
    /// <summary>
    /// 将依赖属性动画一个相对增量
    /// </summary>
    /// <param name="target"></param>
    /// <param name="property"></param>
    /// <param name="deltaValue"></param>
    /// <param name="duration"></param>
    /// <param name="easing"></param>
    /// <returns></returns>
    public LABuilder By(
        DependencyObject target, DependencyProperty property, double deltaValue,
        double? duration = null, Func<double, double>? easing = null)
    {
        Add(new DependencyPropertyLA(target, property, deltaValue, true,
            duration ?? _defaultDuration, ConsumeDelay(),
            easing ?? _defaultEasing, ConsumeBarrier()));
        return this;
    }


    // ScaleTransform 动画(X/Y 同步)

    /// <summary>
    /// 将 ScaleTransform 同步缩放到绝对目标值
    /// </summary>
    /// <param name="transform"></param>
    /// <param name="endScale"></param>
    /// <param name="duration"></param>
    /// <param name="easing"></param>
    /// <returns></returns>
    public LABuilder Scale(
        ScaleTransform transform, double endScale,
        double? duration = null, Func<double, double>? easing = null)
    {
        Add(new ScaleTransformLA(transform, endScale, false,
            duration ?? _defaultDuration, ConsumeDelay(),
            easing ?? _defaultEasing, ConsumeBarrier()));
        return this;
    }

    /// <summary>
    /// 将 ScaleTransform 同步缩放一个相对增量
    /// </summary>
    /// <param name="transform"></param>
    /// <param name="deltaScale"></param>
    /// <param name="duration"></param>
    /// <param name="easing"></param>
    /// <returns></returns>
    public LABuilder ScaleBy(
        ScaleTransform transform, double deltaScale,
        double? duration = null, Func<double, double>? easing = null)
    {
        Add(new ScaleTransformLA(transform, deltaScale, true,
            duration ?? _defaultDuration, ConsumeDelay(),
            easing ?? _defaultEasing, ConsumeBarrier()));
        return this;
    }


    // RotateTransform 动画

    /// <summary>
    /// 将 RotateTransform 旋转到绝对目标角度 (度)
    /// </summary>
    /// <param name="transform"></param>
    /// <param name="endAngle"></param>
    /// <param name="duration"></param>
    /// <param name="easing"></param>
    /// <returns></returns>
    public LABuilder Rotate(
        RotateTransform transform, double endAngle,
        double? duration = null, Func<double, double>? easing = null)
    {
        Add(new RotateTransformLA(transform, endAngle, false,
            duration ?? _defaultDuration, ConsumeDelay(),
            easing ?? _defaultEasing, ConsumeBarrier()));
        return this;
    }

    /// <summary>
    /// 将 RotateTransform 旋转一个相对角度增量 (度)
    /// </summary>
    /// <param name="transform"></param>
    /// <param name="deltaAngle"></param>
    /// <param name="duration"></param>
    /// <param name="easing"></param>
    /// <returns></returns>
    public LABuilder RotateBy(
        RotateTransform transform, double deltaAngle,
        double? duration = null, Func<double, double>? easing = null)
    {
        Add(new RotateTransformLA(transform, deltaAngle, true,
            duration ?? _defaultDuration, ConsumeDelay(),
            easing ?? _defaultEasing, ConsumeBarrier()));
        return this;
    }


    // TranslateTransform 动画 (X/Y联动)

    /// <summary>
    /// 将 TranslateTransform 移动到绝对目标坐标 (endX, endY)
    /// </summary>
    /// <param name="transform"></param>
    /// <param name="endX"></param>
    /// <param name="endY"></param>
    /// <param name="duration"></param>
    /// <param name="easing"></param>
    /// <returns></returns>
    public LABuilder Move(
        TranslateTransform transform, double endX, double endY,
        double? duration = null, Func<double, double>? easing = null)
    {
        double dur = duration ?? _defaultDuration;
        Func<double, double> ez = easing ?? _defaultEasing;
        double delay = ConsumeDelay();
        bool barrier = ConsumeBarrier();

        // X,Y 作为两个并行子动作,只有 X 承担序列门与延迟, Y 与之并行
        Add(new DependencyPropertyLA(transform, TranslateTransform.XProperty,
                endX, false, dur, delay, ez, barrier));
        Add(new DependencyPropertyLA(transform, TranslateTransform.YProperty,
            endY, false, dur, delay, ez, false));
        return this;
    }

    /// <summary>
    /// 将 TranslateTransform 移动一个相对增量 (deltaX, deltaY)
    /// </summary>
    /// <param name="transform"></param>
    /// <param name="deltaX"></param>
    /// <param name="deltaY"></param>
    /// <param name="duration"></param>
    /// <param name="easing"></param>
    /// <returns></returns>
    public LABuilder MoveBy(
            TranslateTransform transform, double deltaX, double deltaY,
            double? duration = null, Func<double, double>? easing = null)
    {
        double dur = duration ?? _defaultDuration;
        Func<double, double> ez = easing ?? _defaultEasing;
        double delay = ConsumeDelay();
        bool barrier = ConsumeBarrier();

        Add(new DependencyPropertyLA(transform, TranslateTransform.XProperty,
            deltaX, true, dur, delay, ez, barrier));
        Add(new DependencyPropertyLA(transform, TranslateTransform.YProperty,
            deltaY, true, dur, delay, ez, false));
        return this;
    }


    // 颜色动画

    /// <summary>
    /// 将 SolidColorBrush 的颜色动画到目标色 (sRGB 四通道线性插值)
    /// </summary>
    /// <param name="brush"></param>
    /// <param name="endColor"></param>
    /// <param name="duration"></param>
    /// <param name="easing"></param>
    /// <returns></returns>
    public LABuilder Color(
        SolidColorBrush brush, Color endColor,
        double? duration = null, Func<double, double>? easing = null)
    {
        Add(new ColorLA(brush, endColor,
                duration ?? _defaultDuration, ConsumeDelay(),
                easing ?? _defaultEasing, ConsumeBarrier()));
        return this;
    }

    /// <summary>
    /// 将元素的 SolidColorBrush 属性动画到目标色 (sRGB 四通道线性插值)
    /// 支持自动处理冻结晶刷
    /// </summary>
    /// <param name="target">目标元素</param>
    /// <param name="property">画刷依赖属性 (如 Shape.FillProperty)</param>
    /// <param name="endColor">目标颜色</param>
    /// <param name="duration">时长(毫秒)</param>
    /// <param name="easing">缓动函数</param>
    /// <returns></returns>
    public LABuilder Color(
        DependencyObject target, DependencyProperty property, Color endColor,
        double? duration = null, Func<double, double>? easing = null)
    {
        var brush = target.GetValue(property) as SolidColorBrush;
        if (brush == null)
            throw new InvalidOperationException($"Property {property.Name} is not a SolidColorBrush");

        Add(new ColorLA(brush, endColor,
                duration ?? _defaultDuration, ConsumeDelay(),
                easing ?? _defaultEasing, ConsumeBarrier(),
                target, property));
        return this;
    }


    // 回调

    /// <summary>
    /// 插入一段代码回调(时长为 0, 可带延迟)  常用于序列中触发状态变更
    /// </summary>
    /// <param name="action"></param>
    /// <returns></returns>
    public LABuilder Callback(Action action)
    {
        Add(new CallbackLA(action, ConsumeDelay(), ConsumeBarrier()));
        return this;
    }


    // SkewTransform 动画

    /// <summary>
    /// 将 SkewTransform 偏斜到绝对目标角度 (度)
    /// </summary>
    /// <param name="transform"></param>
    /// <param name="endAngle"></param>
    /// <param name="duration"></param>
    /// <param name="easing"></param>
    /// <returns></returns>
    public LABuilder Skew(
        SkewTransform transform, double endAngle,
        double? duration = null, Func<double, double>? easing = null)
    {
        Add(new SkewTransformLA(transform, endAngle, false,
            duration ?? _defaultDuration, ConsumeDelay(),
            easing ?? _defaultEasing, ConsumeBarrier()));
        return this;
    }

    /// <summary>
    /// 将 SkewTransform 偏斜一个相对角度增量 (度)
    /// </summary>
    /// <param name="transform"></param>
    /// <param name="deltaAngle"></param>
    /// <param name="duration"></param>
    /// <param name="easing"></param>
    /// <returns></returns>
    public LABuilder SkewBy(
        SkewTransform transform, double deltaAngle,
        double? duration = null, Func<double, double>? easing = null)
    {
        Add(new SkewTransformLA(transform, deltaAngle, true,
            duration ?? _defaultDuration, ConsumeDelay(),
            easing ?? _defaultEasing, ConsumeBarrier()));
        return this;
    }


    // 常用属性动画快捷方法

    /// <summary>
    /// 将元素的 Opacity 动画到目标值 (0~1)
    /// </summary>
    public LABuilder Opacity(
        UIElement target, double endValue,
        double? duration = null, Func<double, double>? easing = null)
    {
        return To(target, UIElement.OpacityProperty, endValue, duration, easing);
    }

    /// <summary>
    /// 将元素的 Opacity 动画一个相对增量
    /// </summary>
    public LABuilder OpacityBy(
        UIElement target, double deltaValue,
        double? duration = null, Func<double, double>? easing = null)
    {
        return By(target, UIElement.OpacityProperty, deltaValue, duration, easing);
    }

    /// <summary>
    /// 将元素的 Width 动画到目标值
    /// </summary>
    public LABuilder Width(
        FrameworkElement target, double endValue,
        double? duration = null, Func<double, double>? easing = null)
    {
        return To(target, FrameworkElement.WidthProperty, endValue, duration, easing);
    }

    /// <summary>
    /// 将元素的 Width 动画一个相对增量
    /// </summary>
    public LABuilder WidthBy(
        FrameworkElement target, double deltaValue,
        double? duration = null, Func<double, double>? easing = null)
    {
        return By(target, FrameworkElement.WidthProperty, deltaValue, duration, easing);
    }

    /// <summary>
    /// 将元素的 Height 动画到目标值
    /// </summary>
    public LABuilder Height(
        FrameworkElement target, double endValue,
        double? duration = null, Func<double, double>? easing = null)
    {
        return To(target, FrameworkElement.HeightProperty, endValue, duration, easing);
    }

    /// <summary>
    /// 将元素的 Height 动画一个相对增量
    /// </summary>
    public LABuilder HeightBy(
        FrameworkElement target, double deltaValue,
        double? duration = null, Func<double, double>? easing = null)
    {
        return By(target, FrameworkElement.HeightProperty, deltaValue, duration, easing);
    }

    /// <summary>
    /// 淡入: 将元素 Opacity 从当前值动画到 1
    /// </summary>
    public LABuilder FadeIn(
        UIElement target,
        double? duration = null, Func<double, double>? easing = null)
    {
        return Opacity(target, 1.0, duration, easing);
    }

    /// <summary>
    /// 淡出: 将元素 Opacity 从当前值动画到 0
    /// </summary>
    public LABuilder FadeOut(
        UIElement target,
        double? duration = null, Func<double, double>? easing = null)
    {
        return Opacity(target, 0.0, duration, easing);
    }


    // 收尾

    /// <summary>
    /// 设置动画组全部完成后的回调
    /// </summary>
    /// <param name="callback"></param>
    /// <returns></returns>
    public LABuilder OnComplete(Action callback)
    {
        _group.OnComplete = callback;
        return this;
    }
    /// <summary>
    /// 构建并返回动画组(不立即启动)
    /// </summary>
    /// <returns></returns>
    public LAGroup BuildGroup() => _group;
    /// <summary>
    /// 注册并启动动画,返回动画组名称,可用于后续 Stop 查询
    /// </summary>
    /// <returns></returns>
    public string Play()
    {
        LAEngine.Play(_group);
        return _group.Name;
    }


    // 内部工具

    private void Add(ILAAction action) => _group.AddAction(action);

    /// <summary>
    /// 消费并清除一次性延迟
    /// </summary>
    /// <returns></returns>
    private double ConsumeDelay()
    {
        double d = _pendingDelay;
        _pendingDelay = 0;
        return d;
    }

    /// <summary>
    /// 消费序列门标记: 返回下一个动作是否等待前序,随后清除
    /// </summary>
    /// <returns></returns>
    private bool ConsumeBarrier()
    {
        bool b = _sequenceBarrier;
        _sequenceBarrier = false;
        return b;
    }
}