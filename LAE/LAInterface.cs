using System.Windows;
using System.Windows.Media;

namespace LAE
{
    /// <summary>
    /// 引擎内部动画动作契约 (非公开 API)
    /// </summary>
    internal interface ILAAction
    {
        /// <summary>动作时长 (毫秒)</summary>
        double DurationMs { get; }
        /// <summary>动作开始前的一次性延迟 (毫秒)</summary>
        double DelayMs { get; }
        /// <summary>序列模式中是否等待前序动作完成</summary>
        bool WaitForPrevious { get; }
        /// <summary>动作是否已完成</summary>
        bool IsDone { get; }
        /// <summary>延迟结束后首次帧调用</summary>
        void OnStart();
        /// <summary>每帧调用, elapsedMs 已扣除延迟</summary>
        void Update(double elapsedMs);
    }

    /// <summary>
    /// 单一数值目标动画基类。
    /// <para>
    /// 采用 <b>绝对写入</b> 模式: 记录起始值与终点值, 每帧写入
    /// <c>start + (end - start) * easing(t)</c>。
    /// 相较逐帧回读属性再累加增量, 该方式没有浮点累积误差,
    /// 且每帧免除一次 <see cref="DependencyObject.GetValue"/> 装箱回读。
    /// </para>
    /// </summary>
    internal abstract class LAActionBase : ILAAction
    {
        private readonly double _targetValue;   // To 模式为绝对终点; By 模式为相对增量
        private readonly bool _relative;        // true = By(相对), false = To(绝对)
        private readonly Func<double, double> _easing;

        protected double StartValue;            // OnStart 时回读的真实起点
        protected double EndValue;              // 解析后的绝对终点

        public double DurationMs { get; }
        public double DelayMs { get; }
        public bool WaitForPrevious { get; }
        public bool IsDone { get; private set; }

        /// <summary>
        /// 构造数值动画。
        /// </summary>
        /// <param name="value">To 模式为绝对终点; By 模式为相对增量</param>
        /// <param name="relative">true=By(相对), false=To(绝对)</param>
        /// <param name="durationMs">时长(毫秒)</param>
        /// <param name="delayMs">延迟(毫秒)</param>
        /// <param name="easing">缓动函数</param>
        /// <param name="waitForPrevious">序列模式中是否等待前序完成</param>
        protected LAActionBase(
            double value, bool relative,
            double durationMs, double delayMs,
            Func<double, double> easing,
            bool waitForPrevious)
        {
            _targetValue = value;
            _relative = relative;
            _easing = easing ?? Easing.Linear;

            // 时长下限 1ms, 避免除零; 延迟不允许为负
            DurationMs = durationMs > 1.0 ? durationMs : 1.0;
            DelayMs = delayMs > 0 ? delayMs : 0;
            WaitForPrevious = waitForPrevious;
        }

        public void OnStart()
        {
            // 幂等: 引擎保证每个动作只调用一次, 重复调用也不会累积偏移
            StartValue = ReadCurrentValue();                 // 回读一次真实起点
            EndValue = _relative ? StartValue + _targetValue : _targetValue;
        }

        public void Update(double elapsedMs)
        {
            if (IsDone) return;

            double t = elapsedMs >= DurationMs ? 1.0 : (elapsedMs <= 0 ? 0.0 : elapsedMs / DurationMs);
            double p = _easing(t);
            if (p < 0) p = 0;
            else if (p > 1) p = 1;

            ApplyValue(StartValue + (EndValue - StartValue) * p);

            if (t >= 1.0) IsDone = true;
        }

        /// <summary>回读目标属性当前值 (仅 OnStart 调用一次)</summary>
        protected abstract double ReadCurrentValue();

        /// <summary>写入本帧的绝对值</summary>
        protected abstract void ApplyValue(double value);
    }

    /// <summary>
    /// 通用依赖属性数值动画 (Move 的 X/Y、Width、Height、Opacity、Scale 等均由此实现)
    /// </summary>
    internal sealed class DependencyPropertyLA : LAActionBase
    {
        private readonly DependencyObject _target;
        private readonly DependencyProperty _property;
        private readonly double _minValue;   // 结果下限, 默认 double.NegativeInfinity

        public DependencyPropertyLA(
            DependencyObject target, DependencyProperty property,
            double value, bool relative,
            double duration, double delayMs,
            Func<double, double> easing, bool waitForPrevious,
            double minValue = double.NegativeInfinity)
            : base(value, relative, duration, delayMs, easing, waitForPrevious)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _property = property ?? throw new ArgumentNullException(nameof(property));
            _minValue = minValue;
        }

        protected override double ReadCurrentValue()
            => Convert.ToDouble(_target.GetValue(_property));

        protected override void ApplyValue(double value)
        {
            if (value < _minValue) value = _minValue;
            _target.SetValue(_property, value);
        }
    }

    /// <summary>
    /// 对 RotateTransform 的 Angle 进行动画
    /// </summary>
    internal sealed class RotateTransformLA : LAActionBase
    {
        private readonly RotateTransform _transform;

        public RotateTransformLA(
            RotateTransform transform,
            double value, bool relative,
            double durationMs, double delayMs,
            Func<double, double> easing, bool waitForPrevious)
            : base(value, relative, durationMs, delayMs, easing, waitForPrevious)
        {
            _transform = transform ?? throw new ArgumentNullException(nameof(transform));
        }

        protected override double ReadCurrentValue() => _transform.Angle;
        protected override void ApplyValue(double value) => _transform.Angle = value;
    }

    /// <summary>
    /// 对 SolidColorBrush 的 Color 进行动画 (sRGB 四通道线性插值)。
    /// <para>
    /// 冻结晶刷会在动画启动时克隆并回设到元素上, 因此构建阶段不会产生
    /// 任何界面副作用; 未冻结的画刷直接就地改写。
    /// </para>
    /// </summary>
    internal sealed class ColorLA : ILAAction
    {
        private readonly DependencyObject? _target;      // 需要回设画刷的目标元素
        private readonly DependencyProperty? _property;  // 画刷依赖属性
        private readonly Color _endColor;
        private readonly Func<double, double> _easing;

        private SolidColorBrush _brush = null!;          // OnStart 解析
        private double _startA, _startR, _startG, _startB;
        private double _endA, _endR, _endG, _endB;
        private bool _started;
        private bool _done;

        public double DurationMs { get; }
        public double DelayMs { get; }
        public bool WaitForPrevious { get; }
        public bool IsDone => _done;

        /// <summary>
        /// 直接对画刷实例做动画。
        /// </summary>
        /// <param name="brush">目标画刷 (会被就地改写)</param>
        public ColorLA(
            SolidColorBrush brush, Color endColor,
            double durationMs, double delayMs,
            Func<double, double> easing, bool waitForPrevious)
            : this(endColor, durationMs, delayMs, easing, waitForPrevious)
        {
            _brush = brush ?? throw new ArgumentNullException(nameof(brush));
        }

        /// <summary>
        /// 对元素的画刷依赖属性做动画, 自动处理冻结晶刷。
        /// </summary>
        /// <param name="target">目标元素</param>
        /// <param name="property">画刷依赖属性 (如 Shape.FillProperty)</param>
        public ColorLA(
            DependencyObject target, DependencyProperty property, Color endColor,
            double durationMs, double delayMs,
            Func<double, double> easing, bool waitForPrevious)
            : this(endColor, durationMs, delayMs, easing, waitForPrevious)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _property = property ?? throw new ArgumentNullException(nameof(property));
        }

        private ColorLA(
            Color endColor, double durationMs, double delayMs,
            Func<double, double> easing, bool waitForPrevious)
        {
            _endColor = endColor;
            _easing = easing ?? Easing.Linear;
            DurationMs = durationMs > 1.0 ? durationMs : 1.0;
            DelayMs = delayMs > 0 ? delayMs : 0;
            WaitForPrevious = waitForPrevious;

            _endA = endColor.A;
            _endR = endColor.R;
            _endG = endColor.G;
            _endB = endColor.B;
        }

        public void OnStart()
        {
            if (_started) return;
            _started = true;

            if (_target != null)
            {
                // 元素画刷: 冻结晶刷不可写, 克隆后回设
                if (_target.GetValue(_property!) is not SolidColorBrush current)
                    throw new InvalidOperationException(
                        $"Property {_property!.Name} is not a SolidColorBrush");

                _brush = current.IsFrozen ? current.CloneCurrentValue() : current;
                if (!ReferenceEquals(_brush, current))
                    _target.SetValue(_property!, _brush);
            }

            Color c = _brush.Color;
            _startA = c.A;
            _startR = c.R;
            _startG = c.G;
            _startB = c.B;
        }

        public void Update(double elapsedMs)
        {
            if (_done) return;

            double t = elapsedMs >= DurationMs ? 1.0 : (elapsedMs <= 0 ? 0.0 : elapsedMs / DurationMs);
            double p = _easing(t);
            if (p < 0) p = 0;
            else if (p > 1) p = 1;

            if (t >= 1.0)
            {
                _brush.Color = _endColor;
                _done = true;
                return;
            }

            _brush.Color = Color.FromArgb(
                Mix(_startA, _endA, p),
                Mix(_startR, _endR, p),
                Mix(_startG, _endG, p),
                Mix(_startB, _endB, p));
        }

        private static byte Mix(double from, double to, double p)
        {
            double v = from + (to - from) * p;
            if (v <= 0) return 0;
            if (v >= 255) return 255;
            return (byte)(v + 0.5);
        }
    }

    /// <summary>
    /// 静止停顿动作 (恒为序列分隔点)
    /// </summary>
    internal sealed class WaitLA : ILAAction
    {
        public double DurationMs { get; }
        public double DelayMs => 0;
        public bool WaitForPrevious { get; }
        public bool IsDone { get; private set; }
        public bool IsZero => DurationMs <= 0;

        public WaitLA(double durationMs, bool waitForPrevious)
        {
            DurationMs = durationMs > 0 ? durationMs : 0;
            WaitForPrevious = waitForPrevious;
        }

        public void OnStart() { }

        public void Update(double elapsedMs)
        {
            if (elapsedMs >= DurationMs)
                IsDone = true;
        }
    }

    /// <summary>
    /// 代码回调动作 (时长 0, 可带延迟)
    /// </summary>
    internal sealed class CallbackLA : ILAAction
    {
        private readonly Action _callback;
        private bool _executed;

        public double DurationMs => 0;
        public double DelayMs { get; }
        public bool WaitForPrevious { get; }
        public bool IsDone => _executed;

        public CallbackLA(Action callback, double delayMs, bool waitForPrevious)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            DelayMs = delayMs > 0 ? delayMs : 0;
            WaitForPrevious = waitForPrevious;
        }

        public void OnStart() { }

        public void Update(double elapsedMs)
        {
            if (_executed) return;
            _executed = true;
            _callback();
        }
    }
}
