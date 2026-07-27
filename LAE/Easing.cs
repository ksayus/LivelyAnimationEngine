namespace LAE;

public static class Easing
{
    // 匀速 f(t) = t
    public static readonly Func<double, double> Linear =
        t => Math.Clamp(t, 0, 1);

    // 缓出 f(t) = 1 - (1 - t)^p
    public static Func<double, double> OutPow(int p = 3) =>
        t => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), p);

    // 缓入缓出: 前半段加速,后半段减速
    public static Func<double, double> InOutPow(int p = 3) =>
        t =>
        {
            t = Math.Clamp(t, 0, 1);
            return t < 0.5
                ? 0.5 * Math.Pow(2 * t, p)
                : 1 - 0.5 * Math.Pow(2 * (1 - t), p);
        };

    // 常用预设
    public static readonly Func<double, double> OutCubic = OutPow(3);
    public static readonly Func<double, double> InOutCubic = InOutPow(3);
}