using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows.Media;

namespace LAE;

public static class LAEngine
{
    private static readonly Dictionary<string, LAGroup> _groups = new();
    private static long _lastTickTicks;         // 上一帧的 Stopwatch 时间戳
    private static bool _hooked;                // 是否已订阅 Rendering
    private static int _freezeCounter;          // 冻结计数器, > 0 时跳过动画更新

    /// <summary>
    /// 全局速度倍率 (0.1 ~ 200) 默认 1.0
    /// </summary>
    public static double Speed { get; set; } = 1.0;

    /// <summary>
    /// 活跃的动画组数量
    /// </summary>
    public static int ActiveGroupCount => _groups.Count;

    /// <summary>
    /// 当前是否被冻结 (批量初始化时使用)
    /// </summary>
    public static bool IsFrozen => _freezeCounter > 0;


    /// <summary>
    /// 注册并启动一个动画组.
    /// 若动画组已存在,自动停止旧组
    /// </summary>
    /// <param name="group"> 要启动的动画组 </param>
    public static void Play(LAGroup group)
    {
        if (group == null) throw new ArgumentNullException(nameof(group));
        if (string.IsNullOrEmpty(group.Name))
            group.Name = $"__anon_{group.Id}__";

        // 同名先停
        Stop(group.Name);
        _groups[group.Name] = group;
        group.OnRegistered();
        EnsureHooked();
    }


    /// <summary>
    /// 停止指定名称的动画组
    /// </summary>
    /// <param name="name"> 要启动的动画组 </param>
    public static void Stop(string name)
    {
        _groups.Remove(name);
    }

    /// <summary>
    /// 查询指定名称的动画组是否在运行
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public static bool IsRunning(string name) => _groups.ContainsKey(name);

    /// <summary>
    /// 停止所有动画组
    /// </summary>
    public static void StopAll()
    {
        _groups.Clear();
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
        if  (_hooked) return;
        _lastTickTicks = Stopwatch.GetTimestamp();
        CompositionTarget.Rendering += OnRendering;
        _hooked = true;
    }

    private static void UnhookIfEmpty()
    {
        if (_groups.Count > 0 || _hooked == false) return;
        CompositionTarget.Rendering -= OnRendering;
        _hooked = false;
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        if (IsFrozen) { _lastTickTicks = Stopwatch.GetTimestamp(); return; }

        long now = Stopwatch.GetTimestamp();
        double elapsedMs = (now - _lastTickTicks) * 1000.0 / Stopwatch.Frequency;
        _lastTickTicks = now;

        // 钳制, 避免窗口最小化恢复后大跳
        double dt = Math.Clamp(elapsedMs * Speed, 0, 1000);

        // 快照, 避免遍历中修改集合
        var snapshot = new List<LAGroup>(_groups.Values);
        foreach (var group in snapshot)
        {
            group.Update(dt);
            if (group.IsCompleted)
                _groups.Remove(group.Name);
        }

        UnhookIfEmpty();
    }
}
