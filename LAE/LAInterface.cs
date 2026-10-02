using System.Windows;
using System.Windows.Media;

namespace LAE
{
    /// <summary>引擎内部的动作契约，不是公开 API</summary>
    internal interface ILAAction
    {
        /// <summary>动作时长（毫秒）</summary>
        double DurationMs { get; }
        /// <summary>动作开始前的一次性延迟（毫秒）</summary>
        double DelayMs { get; }
        /// <summary>序列模式下是否等前序动作完成</summary>
        bool WaitForPrevious { get; }
        /// <summary>动作是否已完成</summary>
        bool IsDone { get; }
        /// <summary>延迟结束后的首次帧调用</summary>
        void OnStart();
        /// <summary>每帧调用，elapsedMs 已经扣掉延迟</summary>
        void Update(double elapsedMs);
    }

    /// <summary>
    /// 单个数值目标的动画基类。
    /// 采用绝对写入：记下起点和终点，每帧写入 <c>start + (end - start) * easing(t)</c>。
    /// 相比逐帧回读属性再累加增量，这样没有浮点累积误差，每帧也省掉一次装箱回读。
    /// </summary>
    internal abstract class LAActionBase : ILAAction
    {
        private readonly double _targetValue;   // To 模式是绝对终点，By 模式是相对增量
        private readonly bool _relative;        // true = By（相对），false = To（绝对）
        private readonly Func<double, double> _easing;

        protected double StartValue;            // OnStart 时回读到的真实起点
        protected double EndValue;              // 解析后的绝对终点

        public double DurationMs { get; }
        public double DelayMs { get; }
        public bool WaitForPrevious { get; }
        public bool IsDone { get; private set; }

        /// <param name="value">To 模式是绝对终点，By 模式是相对增量</param>
        /// <param name="relative">true 表示 By（相对），false 表示 To（绝对）</param>
        protected LAActionBase(
            double value, bool relative,
            double durationMs, double delayMs,
            Func<double, double> easing,
            bool waitForPrevious)
        {
            _targetValue = value;
            _relative = relative;
            _easing = easing ?? Easing.Linear;

            // 时长下限 1ms，避免除零；延迟不允许为负
            DurationMs = durationMs > 1.0 ? durationMs : 1.0;
            DelayMs = delayMs > 0 ? delayMs : 0;
            WaitForPrevious = waitForPrevious;
        }

        public void OnStart()
        {
            // 引擎保证每个动作只调一次 OnStart；即便重复调用也不会累积偏移
            StartValue = ReadCurrentValue();
            EndValue = _relative ? StartValue + _targetValue : _targetValue;
        }

        public void Update(double elapsedMs)
        {
            if (IsDone) return;

            double t = elapsedMs >= DurationMs ? 1.0 : (elapsedMs <= 0 ? 0.0 : elapsedMs / DurationMs);

            // 缓动输出不做 [0,1] 钳制：回弹、弹性这类曲线本来就要越过终点，
            // 属性自身的取值范围由各动作的上下界负责（见 DependencyPropertyLA）。
            ApplyValue(StartValue + (EndValue - StartValue) * _easing(t));

            if (t >= 1.0) IsDone = true;
        }

        /// <summary>回读目标属性的当前值，只在 OnStart 调一次</summary>
        protected abstract double ReadCurrentValue();

        /// <summary>写入本帧的绝对值</summary>
        protected abstract void ApplyValue(double value);
    }

    /// <summary>
    /// 通用的依赖属性数值动画，Move 的 X/Y、Width、Height、Opacity、Scale 都走这里。
    /// </summary>
    internal sealed class DependencyPropertyLA : LAActionBase
    {
        private readonly DependencyObject _target;
        private readonly DependencyProperty _property;
        private readonly double _minValue;   // 结果下限，默认 -∞
        private readonly double _maxValue;   // 结果上限，默认 +∞

        public DependencyPropertyLA(
            DependencyObject target, DependencyProperty property,
            double value, bool relative,
            double duration, double delayMs,
            Func<double, double> easing, bool waitForPrevious,
            double minValue = double.NegativeInfinity,
            double maxValue = double.PositiveInfinity)
            : base(value, relative, duration, delayMs, easing, waitForPrevious)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _property = property ?? throw new ArgumentNullException(nameof(property));
            _minValue = minValue;
            _maxValue = maxValue;
        }

        protected override double ReadCurrentValue()
            => Convert.ToDouble(_target.GetValue(_property));

        protected override void ApplyValue(double value)
        {
            if (value < _minValue) value = _minValue;
            else if (value > _maxValue) value = _maxValue;
            _target.SetValue(_property, value);
        }
    }

    /// <summary>对 RotateTransform.Angle 做动画</summary>
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
    /// 对 SolidColorBrush.Color 做动画，sRGB 四通道线性插值。
    /// 冻结的画刷在动画启动时克隆并回设到元素上，构建阶段不产生副作用；
    /// 没冻结的画刷直接就地改写。
    /// </summary>
    internal sealed class ColorLA : ILAAction
    {
        private readonly DependencyObject? _target;      // 需要回设画刷的目标元素
        private readonly DependencyProperty? _property;  // 画刷依赖属性
        private readonly Color _endColor;
        private readonly Func<double, double> _easing;

        private SolidColorBrush _brush = null!;          // OnStart 时解析
        private double _startA, _startR, _startG, _startB;
        private double _endA, _endR, _endG, _endB;
        private bool _started;
        private bool _done;

        public double DurationMs { get; }
        public double DelayMs { get; }
        public bool WaitForPrevious { get; }
        public bool IsDone => _done;

        /// <summary>直接对画刷实例做动画</summary>
        /// <param name="brush">目标画刷，会被就地改写</param>
        public ColorLA(
            SolidColorBrush brush, Color endColor,
            double durationMs, double delayMs,
            Func<double, double> easing, bool waitForPrevious)
            : this(endColor, durationMs, delayMs, easing, waitForPrevious)
        {
            _brush = brush ?? throw new ArgumentNullException(nameof(brush));
        }

        /// <summary>对元素上的画刷属性做动画，冻结的画刷会自动克隆</summary>
        /// <param name="property">画刷依赖属性，例如 <c>Shape.FillProperty</c></param>
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
                // 元素上的画刷：冻结的不可写，克隆一份再回设
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
            double p = _easing(t);   // 不钳制，超调由 Mix 的四通道区间兜住

            // 最后一帧直接落到目标色，避免插值尾数留零头
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

    /// <summary>静止停顿动作，永远是序列分隔点</summary>
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

    /// <summary>代码回调动作，时长为 0，可以带延迟</summary>
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
