namespace LAE;

/// <summary>
/// 缓动函数集合。
/// <para>
/// 所有缓动都保证 <c>f(0)=0</c>、<c>f(1)=1</c>, 且输入自动钳制到 [0,1]。
/// 整数幂次使用多项式展开而非 <see cref="Math.Pow"/>, 避免每帧的超越函数开销。
/// </para>
/// </summary>
public static class Easing
{
    /// <summary>匀速 f(t) = t</summary>
    public static readonly Func<double, double> Linear = static t =>
        t <= 0 ? 0 : (t >= 1 ? 1 : t);

    /// <summary>
    /// 缓出 f(t) = 1 - (1 - t)^p, p 为指数 (越大起步越快、收尾越缓)
    /// </summary>
    public static Func<double, double> OutPow(int p = 3)
    {
        if (p <= 1) return Linear;

        if (p == 2) return static t =>
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            double u = 1 - t;
            return 1 - u * u;
        };

        if (p == 3) return static t =>
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            double u = 1 - t;
            return 1 - u * u * u;
        };

        if (p == 4) return static t =>
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            double u = 1 - t, u2 = u * u;
            return 1 - u2 * u2;
        };

        if (p == 5) return static t =>
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            double u = 1 - t, u2 = u * u;
            return 1 - u2 * u2 * u;
        };

        // 任意指数: 用整数幂的平方求幂法代替 Math.Pow
        return t =>
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            return 1 - IntPow(1 - t, p);
        };
    }

    /// <summary>
    /// 缓入缓出: 前半段加速, 后半段减速
    /// </summary>
    public static Func<double, double> InOutPow(int p = 3)
    {
        if (p <= 1) return Linear;

        if (p == 2) return static t =>
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            if (t < 0.5)
            {
                double x = 2 * t;
                return 0.5 * x * x;
            }
            else
            {
                double u = 2 - 2 * t;
                return 1 - 0.5 * u * u;
            }
        };

        if (p == 3) return static t =>
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            if (t < 0.5)
            {
                double x = 2 * t;
                return 0.5 * x * x * x;
            }
            else
            {
                double u = 2 - 2 * t;
                return 1 - 0.5 * u * u * u;
            }
        };

        if (p == 4) return static t =>
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            if (t < 0.5)
            {
                double x2 = 2 * t;
                x2 *= x2;
                return 0.5 * x2 * x2;
            }
            else
            {
                double u = 2 - 2 * t, u2 = u * u;
                return 1 - 0.5 * u2 * u2;
            }
        };

        if (p == 5) return static t =>
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            if (t < 0.5)
            {
                double x = 2 * t, x2 = x * x;
                return 0.5 * x2 * x2 * x;
            }
            else
            {
                double u = 2 - 2 * t, u2 = u * u;
                return 1 - 0.5 * u2 * u2 * u;
            }
        };

        return t =>
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            return t < 0.5
                ? 0.5 * IntPow(2 * t, p)
                : 1 - 0.5 * IntPow(2 - 2 * t, p);
        };
    }

    /// <summary>缓出 (三次), 引擎默认缓动</summary>
    public static readonly Func<double, double> OutCubic = OutPow(3);

    /// <summary>缓入缓出 (三次)</summary>
    public static readonly Func<double, double> InOutCubic = InOutPow(3);

    /// <summary>
    /// 整数幂的平方求幂: O(log n) 次乘法, 无 Math.Pow 调用开销
    /// </summary>
    private static double IntPow(double x, int n)
    {
        double result = 1.0;
        double factor = x;
        while (n > 0)
        {
            if ((n & 1) != 0) result *= factor;
            n >>= 1;
            if (n > 0) factor *= factor;
        }
        return result;
    }
}
