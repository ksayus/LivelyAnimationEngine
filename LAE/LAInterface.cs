using System.Windows;
using System.Windows.Media;

namespace LAE
{
    internal interface ILAAction
    {
        double DurationMs { get; }
        double DelayMs { get; }
        bool WaitForPrevious { get; } // 序列模式中是否等前序完成
        bool IsDone { get; }
        void Update(double elapsedMs); // 每帧调用
        void OnStart(); // 延迟结束后首次帧
    }

    internal abstract class LAActionBase : ILAAction
    {
        protected Func<double, double> EasingFn;
        protected double _startValue;       // OnStart 抽象
        private double _endValue;           // 终点
        private double _relativeDelta;      // 总量值
        private bool _relative;             // true = By false = To
        private double _lastProgress;       // 上一帧进度
        private bool _started;

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
            _relative = relative;
            if (relative)
            {
                _relativeDelta = value;
                _endValue = 0; // OnStart 时推导
            }else
            {
                _endValue = value;
                _relativeDelta = 0;
            }

            DurationMs = Math.Max(durationMs, 1);
            DelayMs = Math.Max(delayMs, 0);
            WaitForPrevious = waitForPrevious;
            EasingFn = easing ?? Easing.Linear;
        }

        public void OnStart()
        {
            if (_started) return;
            _started = true;
            _startValue = ReadCurrentValue();   // 回读真实值
            if (_relative)
                _endValue = _startValue + _relativeDelta;
        }

        public void Update(double elapsedMs)
        {
            if (IsDone) return;

            double t = Math.Clamp(elapsedMs / DurationMs, 0, 1);
            double progress = EasingFn(t);
            double frameDelta = (progress - _lastProgress)
                                * (_endValue - _startValue);
            _lastProgress = progress;
            ApplyValue(frameDelta); //只写增量
            if (t >= 1.0) IsDone = true;
        }

        protected abstract double ReadCurrentValue();
        protected abstract void ApplyValue(double delta);
    }

    internal sealed class DependencyPropertyLA : LAActionBase
    {
        private readonly DependencyObject _target;
        private readonly DependencyProperty _property;

        public DependencyPropertyLA(
            DependencyObject target, DependencyProperty property,
            double value, bool relative,
            double duration, double delayMs,
            Func<double, double> easing, bool waitForPrevious)
            : base(value, relative, duration, delayMs, easing, waitForPrevious)
        {
            _target = target;
            _property = property;
        }

        protected override double ReadCurrentValue()
        {
            var val = _target.GetValue(_property);
            return Convert.ToDouble(val);
        }

        protected override void ApplyValue(double delta)
        {
            double current = ReadCurrentValue();
            _target.SetValue(_property, current + delta);
        }
    }

    /// <summary>
    /// 对 ScaleTransform 的 ScaleX/ScaleY 进行同步缩放动画
    /// </summary>
    internal sealed class ScaleTransformLA : LAActionBase
    {
        private readonly ScaleTransform _transform;

        public ScaleTransformLA(
            ScaleTransform transform,
            double value, bool relative,
            double durationMs, double delayMs,
            Func<double, double> easing, bool waitForPrevious)
            : base(value, relative, durationMs, delayMs, easing, waitForPrevious)
        {
            _transform = transform;
        }

        protected override double ReadCurrentValue() => _transform.ScaleX;
        protected override void ApplyValue(double delta)
        {
            _transform.ScaleX = Math.Max(_transform.ScaleX + delta, 0);
            _transform.ScaleY = Math.Max(_transform.ScaleY + delta, 0);
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
            _transform = transform;
        }

        protected override double ReadCurrentValue() => _transform.Angle;
        protected override void ApplyValue(double delta)
        {
            _transform.Angle += delta;
        }
    }

    /// <summary>
    /// 对 SkewTransform 的 AngleX/AngleY 进行偏斜动画
    /// </summary>
    internal sealed class SkewTransformLA : LAActionBase
    {
        private readonly SkewTransform _transform;

        public SkewTransformLA(
            SkewTransform transform,
            double value, bool relative,
            double durationMs, double delayMs,
            Func<double, double> easing, bool waitForPrevious)
            : base(value, relative, durationMs, delayMs, easing, waitForPrevious)
        {
            _transform = transform;
        }

        protected override double ReadCurrentValue() => _transform.AngleX;
        protected override void ApplyValue(double delta)
        {
            _transform.AngleX += delta;
            _transform.AngleY += delta;
        }
    }

    /// <summary>
    /// 对 SolidColorBrush 的 Color 进行动画
    /// 在sRGB 空间按 A/R/G/B 四通道线性插值, 实现颜色平滑过渡
    /// 直接写 brush.Color, 要求画刷未被冻结
    /// 若传入 target + property, 冻结晶刷克隆后自动回设到元素上
    /// </summary>
    internal sealed class ColorLA : ILAAction
    {
        private readonly SolidColorBrush _brush;
        private readonly Color _endColor;
        private readonly Func<double, double> _easing;
        private Color _startColor;
        private bool _started;
        private bool _done;

        public double DurationMs { get; }
        public double DelayMs { get; }
        public bool WaitForPrevious { get; }
        public bool IsDone => _done;

        public ColorLA(
            SolidColorBrush brush, Color endColor,
            double durationMs, double delayMs,
            Func<double, double> easing, bool waitForPrevious,
            DependencyObject? target = null, DependencyProperty? property = null)
        {
            if (brush.IsFrozen)
            {
                brush = brush.Clone();
                // 将克隆体回设到元素, 确保动画可见
                if (target != null && property != null)
                    target.SetValue(property, brush);
            }

            _brush = brush ?? throw new ArgumentNullException(nameof(brush));
            _endColor = endColor;
            DurationMs = Math.Max(durationMs, 1);
            DelayMs = Math.Max(delayMs, 0);
            _easing = easing ?? Easing.Linear;
            WaitForPrevious = waitForPrevious;
        }

        public void OnStart()
        {
            if (_started) return;
            _started = true;
            _startColor = _brush.Color;
        }

        public void Update(double elapsedMs)
        {
            if (_done) return;

            double t = Math.Clamp(elapsedMs / DurationMs, 0, 1);
            double p = _easing(t);
            _brush.Color = LerpColor(_startColor, _endColor, p);

            if (t >= 1.0)
            {
                _brush.Color = _endColor;
                _done = true;
            }
        }

        private static Color LerpColor(Color a, Color b, double p)
        {
            return Color.FromArgb(
                (byte)Math.Round(a.A + (b.A - a.A) * p),
                (byte)Math.Round(a.R + (b.R - a.R) * p),
                (byte)Math.Round(a.G + (b.G - a.G) * p),
                (byte)Math.Round(a.B + (b.B - a.B) * p));
        }
    }

    internal sealed class WaitLA : ILAAction
    {
        public double DurationMs { get; }
        public double DelayMs => 0;
        public bool WaitForPrevious { get; }
        public bool IsDone { get; private set; }

        public WaitLA(double durationMs, bool waitForPrevious)
        {
            DurationMs = Math.Max(durationMs, 0);
            WaitForPrevious = waitForPrevious;
        }

        public void OnStart() { }

        public void Update(double elapsedMs)
        {
            if (elapsedMs >= DurationMs)
                IsDone = true;
        }
    }

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
            DelayMs = delayMs;
            WaitForPrevious = waitForPrevious;
        }

        public void OnStart() { }

        public void Update(double elapsedMs)
        {
            if (!_executed)
            {
                _executed = true;
                _callback();
            }
        }
    }
}
