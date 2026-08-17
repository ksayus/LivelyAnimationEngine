using System.Diagnostics;
using System.Windows.Media;

namespace LAE;

/// <summary>
/// 全局动画引擎: 负责动画组的注册、注销与帧驱动。
/// <para>
/// 帧驱动基于 <see cref="CompositionTarget.Rendering"/>，
/// 有动画时订阅、无动画时自动退订, 空闲时零开销。
/// </para>
/// </summary>
public static class LAEngine
{
    private const double MinSpeed = 0.1;
    private const double MaxSpeed = 200.0;
    private const double MaxFrameMs = 1000.0;   // 帧间隔上限, 避免窗口最小化恢复后大跳

    private static readonly Dictionary<string, LAGroup> _groups = new(StringComparer.Ordinal);

    /// <summary>每帧迭代用的稳定视图, 仅在组集合变化时重建</summary>
    private static readonly List<LAGroup> _frameList = new();

    private static bool _frameListDirty = true;
    private static long _lastTickTicks;
    private static bool _hooked;
    private static int _freezeCounter;
    private static double _speed = 1.0;
    private static Exception? _lastError;

    /// <summary>
    /// 全局速度倍率 (0.1 ~ 200), 默认 1.0。
    /// 赋值会被钳制到合法区间, 且不受 <see cref="Freeze"/> 影响。
    /// </summary>
    public static double Speed
    {
        get => _speed;
        set
        {
            if (double.IsNaN(value)) return;
            _speed = value < MinSpeed ? MinSpeed : (value > MaxSpeed ? MaxSpeed : value);
        }
    }

    /// <summary>
    /// 活跃的动画组数量
    /// </summary>
    public static int ActiveGroupCount => _groups.Count;

    /// <summary>
    /// 当前是否被冻结 (批量初始化时使用)
    /// </summary>
    public static bool IsFrozen => _freezeCounter > 0;

    /// <summary>
    /// 帧驱动当前是否处于订阅状态。无动画时应为 false (空闲零开销)。
    /// </summary>
    public static bool IsHooked => _hooked;

    /// <summary>
    /// 最近一次被隔离的动画/回调异常; 无异常时为 null。
    /// 单个动作抛异常不会中断其它动画组, 也不会让引擎卡死。
    /// </summary>
    public static Exception? LastError => _lastError;

    /// <summary>
    /// 清除 <see cref="LastError"/>
    /// </summary>
    public static void ClearLastError() => _lastError = null;

    /// <summary>
    /// 注册并启动一个动画组。
    /// 若同名动画组已存在, 自动停止旧组 (旧组已生效的属性值保留)。
    /// </summary>
    /// <param name="group"> 要启动的动画组 </param>
    public static void Play(LAGroup group)
    {
        if (group == null) throw new ArgumentNullException(nameof(group));
        if (string.IsNullOrEmpty(group.Name))
            group.Name = $"__anon_{group.Id}__";

        // 同名先停, 再复位并接管
        _groups.Remove(group.Name);
        group.OnRegistered();
        _groups[group.Name] = group;

        _frameListDirty = true;
        EnsureHooked();
    }

    /// <summary>
    /// 停止指定名称的动画组。已应用的属性值保持不变。
    /// </summary>
    /// <param name="name"> 动画组名称 </param>
    public static void Stop(string name)
    {
        if (name == null) return;
        if (_groups.Remove(name))
            _frameListDirty = true;
    }

    /// <summary>
    /// 查询指定名称的动画组是否在运行
    /// </summary>
    /// <param name="name"> 动画组名称 </param>
    public static bool IsRunning(string name)
        => name != null && _groups.ContainsKey(name);

    /// <summary>
    /// 停止所有动画组
    /// </summary>
    public static void StopAll()
    {
        if (_groups.Count == 0) return;
        _groups.Clear();
        _frameListDirty = true;
    }

    /// <summary>
    /// 冻结动画更新 (计数器 + 1) 用于批量设置属性时跳过动画
    /// </summary>
    public static void Freeze() => _freezeCounter++;

    /// <summary>
    /// 解冻动画更新 (计数器 - 1) 与 Freeze 配对使用
    /// </summary>
    public static void Unfreeze()
    {
        if (_freezeCounter > 0) _freezeCounter--;
    }


    // ──────────────────────────────────────────────
    //  帧驱动
    // ──────────────────────────────────────────────

    private static void EnsureHooked()
    {
        if (_hooked) return;
        _lastTickTicks = Stopwatch.GetTimestamp();
        CompositionTarget.Rendering += OnRendering;
        _hooked = true;
    }

    private static void UnhookIfEmpty()
    {
        if (!_hooked || _groups.Count > 0) return;
        CompositionTarget.Rendering -= OnRendering;
        _hooked = false;
        _frameList.Clear();
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        double elapsedMs = (now - _lastTickTicks) * 1000.0 / Stopwatch.Frequency;
        _lastTickTicks = now;

        // 冻结期间不推进动画, 但仍持续刷新时间基准
        if (_freezeCounter > 0) return;
        if (elapsedMs <= 0) return;

        double dt = elapsedMs * _speed;
        if (dt > MaxFrameMs) dt = MaxFrameMs;

        if (_frameListDirty)
        {
            _frameList.Clear();
            foreach (var g in _groups.Values) _frameList.Add(g);
            _frameListDirty = false;
        }

        // 倒序遍历: 移除当前元素不影响未访问的下标;
        // 回调中新增的组本帧不推进, 下一帧自然纳入。
        for (int i = _frameList.Count - 1; i >= 0; i--)
        {
            var group = _frameList[i];

            // 该组可能已在回调中被同名的组替换, 此时跳过以免推进到废弃对象
            if (!_groups.TryGetValue(group.Name, out var current) || !ReferenceEquals(current, group))
                continue;

            try
            {
                group.Update(dt);
            }
            catch (Exception ex)
            {
                // 隔离单个动画组的异常, 防止整个引擎停摆
                _lastError = ex;
                _groups.Remove(group.Name);
                _frameListDirty = true;
                continue;
            }

            if (group.IsCompleted)
            {
                _groups.Remove(group.Name);
                _frameListDirty = true;

                var onComplete = group.OnComplete;
                if (onComplete != null)
                {
                    try
                    {
                        onComplete();
                    }
                    catch (Exception ex)
                    {
                        _lastError = ex;
                    }
                }
            }
        }

        UnhookIfEmpty();
    }
}
