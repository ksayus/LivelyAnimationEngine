namespace LAE;

/// <summary>
/// 缓动函数集合。
/// 所有曲线都满足 f(0)=0、f(1)=1，输入超出 [0,1] 时按端点钳制。
/// 整数幂次展开成多项式，不走 Math.Pow。
/// </summary>
public static class Easing
{
    /// <summary>匀速 f(t) = t</summary>
    public static readonly Func<double, double> Linear = static t =>
        t <= 0 ? 0 : (t >= 1 ? 1 : t);

    /// <summary>缓出 f(t) = 1 - (1-t)^p，p 越大起步越快、收尾越缓</summary>
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

        // 任意指数：走整数幂的平方求幂，避免 Math.Pow
        return t =>
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            return 1 - IntPow(1 - t, p);
        };
    }

    /// <summary>缓入缓出：前半段加速，后半段减速</summary>
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

    /// <summary>三次缓出，引擎默认缓动</summary>
    public static readonly Func<double, double> OutCubic = OutPow(3);

    /// <summary>三次缓入缓出</summary>
    public static readonly Func<double, double> InOutCubic = InOutPow(3);

    /// <summary>
    /// 回弹缓出：冲过终点再拉回来，适用于缩放、位移这类允许短暂超调的目标。
    /// f(t) = 1 + (c+1)(t-1)^3 + c(t-1)^2，曲线不单调，t≈0.7 附近会超过 1。
    /// 用在 Opacity 这类有硬性取值范围的属性上会被结果边界夹掉，弹不起来。
    /// </summary>
    /// <param name="overshoot">超调强度，1.2 微弹，2.5 明显回弹；1.70158 是该缓动族的经典值</param>
    public static Func<double, double> OutBack(double overshoot = 1.70158)
    {
        double c = overshoot;
        double c1 = c + 1;
        return t =>
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            double u = t - 1;
            return 1 + c1 * u * u * u + c * u * u;
        };
    }

    /// <summary>
    /// 弹性缓出：终点附近做阻尼振荡，尾巴比 <see cref="OutBack"/> 长。
    /// 本文件里唯一需要超越函数的曲线（振荡离不开正弦项），只在需要强调落定的
    /// 动效上显式选用。同样不单调，用在有硬性取值范围的属性上同样弹不起来。
    /// </summary>
    /// <param name="oscillations">振荡次数，越大尾音越长</param>
    /// <param name="decay">衰减速度，越大越快稳定</param>
    public static Func<double, double> OutElastic(int oscillations = 3, double decay = 8.0)
    {
        // 振幅固定为 1：小于 1 看不出超调，大于 1 会在 t=0 附近把值甩出 [0,1]
        const double amplitude = 1.0;
        double period = oscillations <= 0 ? 1.0 : 2 * Math.PI / oscillations;
        double d = Math.Max(decay, 0.001);
        return t =>
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            return 1 + amplitude * Math.Pow(2, -d * t) * Math.Sin((t - period / 4) * (2 * Math.PI / period));
        };
    }

    /// <summary>回弹缓入缓出：起手和收尾各超调一次，适合切换类动效</summary>
    public static Func<double, double> InOutBack(double overshoot = 1.70158)
    {
        double c = overshoot * 1.525;
        return t =>
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            double x = 2 * t;
            if (x < 1)
            {
                double u = x - 1;
                return 0.5 * (u * u * ((c + 1) * u + c));
            }
            else
            {
                double u = x - 2;
                return 0.5 * (u * u * ((c + 1) * u + c) + 2);
            }
        };
    }

    /// <summary>整数幂的平方求幂，O(log n) 次乘法，不调 Math.Pow</summary>
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
