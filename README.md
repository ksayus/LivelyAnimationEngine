# LAE — Lively Animation Engine

一个轻量、高性能的 WPF 动画引擎，提供流式 API 构建复杂动画序列，支持并行与串行编排、丰富的缓动函数、以及全局速度控制。

> 已使用的项目 [OmniArk](https://github.com/ksayus/OmniArk)

## 特性

- **流式 Builder API** — 链式调用，代码即动画描述
- **并行 & 序列** — 默认并行动画，`Then()` 分隔序列
- **丰富的变换支持** — Move、Scale、Rotate、Skew、Opacity、Width、Height、Color
- **颜色动画** — sRGB 四通道线性插值，自动处理冻结晶刷
- **缓动函数** — Linear、OutCubic、InOutCubic、OutPow(n)、InOutPow(n)
- **全局速度控制** — 实时调速 0.1x ~ 200x
- **冻结/解冻** — 批量设置属性时跳过动画更新
- **命名动画组** — 按名称管理动画生命周期
- **异常隔离** — 单个动画组出错不会拖垮整帧，也不会让引擎停摆
- **零依赖** — 仅依赖 WPF，无第三方库

## 快速开始

```csharp
using LAE;

// 最基本的用法：移动一个元素
LA.Builder()
    .MoveBy(translateTransform, 100, 0, 600)
    .Play();

// 并行：同时缩放 + 旋转 + 变色
LA.Builder("parallel_demo")
    .Scale(scaleTransform, 1.5, 400)
    .RotateBy(rotateTransform, 90, 400)
    .Color(brush, Colors.Red, 400)
    .Play();

// 序列：先缩放 → 再移动 → 再旋转 → 最后变色
LA.Builder("sequence_demo")
    .Scale(scaleTransform, 1.5, 300)
    .Then()
    .MoveBy(translateTransform, 80, 0, 500)
    .Then()
    .RotateBy(rotateTransform, 180, 500)
    .Then()
    .Color(brush, Colors.Orange, 400)
    .OnComplete(() => Console.WriteLine("序列完成!"))
    .Play();
```

## 项目结构

```
LivelyAnimationEngine/
├── LAE.slnx                       # 解决方案 (UI / LAE / Perf)
├── LAE/                           # 核心引擎库
│   ├── LivelyAnimationEngine.csproj
│   ├── LAEngine.cs                # 全局引擎 (Play/Stop/Freeze/Speed/错误隔离)
│   ├── LAGroup.cs                 # 动画组 (步骤门 / 每帧预算 / 帧推进)
│   ├── LABuilder.cs               # 流式构建器 (公开 API)
│   ├── LAInterface.cs             # 内部动画实现 (依赖属性/变换/颜色/回调)
│   ├── Easing.cs                  # 缓动函数
│   ├── InternalsVisibleTo.cs      # 向验证程序集开放内部帧推进
│   ├── DebugHost/                 # Debug 专用宿主入口 (Release 不编译)
│   └── Properties/launchSettings.json
├── UI/                            # 演示台 (推荐启动项目)
│   ├── UI.csproj
│   ├── App.xaml                   # 深色主题与控件样式
│   └── MainWindow.xaml(.cs)       # 33 个演示 + 实时代码/日志/性能面板
└── Perf/                          # 引擎行为验证
    ├── Perf.csproj
    └── Program.cs                 # 148 项断言 + 并发基准 + 新旧帧循环对比
```

## 运行

```bash
# 推荐：完整演示台
dotnet run --project UI/UI.csproj

# 引擎行为验证（148 项断言 + 并发性能基准）
dotnet run --project Perf/Perf.csproj

# 与优化前帧循环的同条件性能对比
dotnet run --project Perf/Perf.csproj -c Release -- --bench

# 类库自身也可以直接启动调试（Debug 配置自带宿主入口）
dotnet run --project LAE/LivelyAnimationEngine.csproj

# 整解决方案
dotnet build LAE.slnx
```

## 调试启动说明

类库默认输出 DLL，**Release 配置保持纯类库输出**，发布与引用不受任何影响。

为了在 Visual Studio 中直接对类库项目按 <kbd>F5</kbd> 调试而不再弹出
“无法直接启动带有‘类库输出类型’的项目，若要调试此项目，请向引用库项目的此解决方案中添加可执行项目”，
`LivelyAnimationEngine.csproj` 在 **Debug 配置**下会额外编译 `DebugHost/` 中的极简宿主入口，
并把输出类型切换为 `WinExe`：

| 配置 | 输出 | 入口 |
|------|------|------|
| `Debug` | `LivelyAnimationEngine.dll` + `LivelyAnimationEngine.exe` | `DebugHost/DebugProgram.cs` |
| `Release` | 仅 `LivelyAnimationEngine.dll` | 无（纯类库） |

- 日常演示与调试建议把 **UI** 设为启动项目；
- 需要恢复纯 DLL 的 Debug 产物时：
  ```bash
  dotnet build LAE/LivelyAnimationEngine.csproj -c Debug -p:DebugAsExecutable=false
  ```

## API 参考

### 构建器入口

```csharp
LA.Builder()          // 匿名动画组
LA.Builder("name")    // 命名动画组, 同名启动时自动停止旧的
```

### 变换动画

| 方法 | 说明 |
|------|------|
| `Move(t, x, y, ms)` | TranslateTransform 移动到绝对坐标（X/Y 同步） |
| `MoveBy(t, dx, dy, ms)` | TranslateTransform 相对移动（X/Y 同步） |
| `Scale(t, s, ms)` | ScaleTransform 缩放到绝对倍率（X/Y 同步） |
| `ScaleBy(t, ds, ms)` | ScaleTransform 相对缩放（X/Y 同步） |
| `Rotate(t, deg, ms)` | RotateTransform 旋转到绝对角度 |
| `RotateBy(t, ddeg, ms)` | RotateTransform 相对旋转 |
| `Skew(t, deg, ms)` | SkewTransform 偏斜到绝对角度（X/Y 同步） |
| `SkewBy(t, ddeg, ms)` | SkewTransform 相对偏斜（X/Y 同步） |

### 属性动画

| 方法 | 说明 |
|------|------|
| `To(target, dp, value, ms)` | 依赖属性动画到目标值 |
| `By(target, dp, delta, ms)` | 依赖属性动画相对增量 |
| `Opacity(target, v, ms)` | `UIElement.Opacity` 到目标值 |
| `OpacityBy(target, dv, ms)` | `UIElement.Opacity` 相对增量 |
| `Width(target, v, ms)` | `FrameworkElement.Width` 到目标值 |
| `WidthBy(target, dv, ms)` | `FrameworkElement.Width` 相对增量 |
| `Height(target, v, ms)` | `FrameworkElement.Height` 到目标值 |
| `HeightBy(target, dv, ms)` | `FrameworkElement.Height` 相对增量 |
| `Color(brush, color, ms)` | SolidColorBrush 颜色动画 |
| `Color(target, dp, color, ms)` | 元素画刷属性颜色动画，自动处理冻结晶刷 |

### 快捷方法

| 方法 | 说明 |
|------|------|
| `FadeIn(target, ms)` | 淡入: Opacity 从当前值到 1 |
| `FadeOut(target, ms)` | 淡出: Opacity 从当前值到 0 |

### 组合控制

| 方法 | 说明 |
|------|------|
| `Then()` | 序列分隔：后续动作等待前序全部完成 |
| `Delay(ms)` | 为下一个动作追加一次性延迟 |
| `Wait(ms)` | 插入一段静止停顿（自动序列分隔） |
| `Callback(action)` | 插入代码回调 |
| `OnComplete(action)` | 动画组全部完成后回调 |
| `During(ms)` | 设置后续动作的默认时长 (默认 300ms) |
| `Ease(fn)` | 设置后续动作的默认缓动函数 (默认 OutCubic) |

### 引擎控制

```csharp
// 播放 / 停止
LAEngine.Play(group);           // 注册并启动动画组
LAEngine.Stop("name");          // 停止指定名称的动画组
LAEngine.StopAll();             // 停止所有动画组
LAEngine.IsRunning("name");     // 查询指定动画组是否在运行

// 全局速度 (赋值会被钳制到 0.1 ~ 200)
LAEngine.Speed = 2.0;

// 冻结 / 解冻 (批量设置属性时防止动画干扰)
LAEngine.Freeze();
// ... 批量设置属性 ...
LAEngine.Unfreeze();

// 状态查询
LAEngine.ActiveGroupCount;      // 活跃动画组数量
LAEngine.IsFrozen;              // 是否处于冻结状态
LAEngine.IsHooked;              // 帧驱动是否已订阅 (空闲时为 false)

// 异常隔离
LAEngine.LastError;             // 最近被隔离的异常, 无异常为 null
LAEngine.ClearLastError();      // 清除
```

### 缓动函数

```csharp
Easing.Linear                   // 匀速
Easing.OutCubic                 // 缓出 (默认)
Easing.InOutCubic               // 缓入缓出
Easing.OutPow(3)                // 自定义指数缓出
Easing.InOutPow(5)              // 自定义指数缓入缓出
```

所有缓动都保证 `f(0)=0`、`f(1)=1`，输入自动钳制到 `[0,1]`；
整数幂走多项式快速路径，不调用 `Math.Pow`。

## 架构设计

```
LA.Builder("name")
  └── LABuilder (流式构建器, 累计动作)
        └── LAGroup (动画组, 管理动作列表 / 步骤门 / 每帧预算)
              ├── DependencyPropertyLA  → 依赖属性动画 (Move/Scale/Skew/Width/Opacity…)
              ├── RotateTransformLA     → 旋转动画
              ├── ColorLA               → 颜色动画
              ├── WaitLA                → 等待动作
              └── CallbackLA            → 回调动作
LAEngine (全局引擎)
  └── CompositionTarget.Rendering 驱动帧更新
```

**帧驱动**：通过 `CompositionTarget.Rendering` 事件驱动，有动画时订阅、无动画时自动取消订阅，
空闲时零开销，且不产生每帧的临时集合分配。

**绝对写入**：数值动画每帧写入 `start + (end - start) * easing(t)`，
而不是累加增量。这样既没有浮点累积误差，也免除了每帧一次
`GetValue` 装箱回读。

**步骤门（序列）**：构建器为每个动作分配一个步骤号，`Then()` / `Wait()` 之后的动作
步骤号更大，必须等更早步骤的全部动作完成后才能开始。同一组合动作（如 `Move` 的 X/Y）
共享一个步骤号，因此会一起等待、一起开始——这避免了 X 已经开始而 Y 还在等门的错位。

**每帧预算**：一帧的总时间预算只被消耗一次。序列放行只能在“前序完成后剩下的”时间里进行，
因此不会出现同一帧内把整条序列快进完的情况。

## UI 演示台

`UI` 项目是一个交互式演示台（深色主题）：

- **左侧演示目录** — 按「变换 / 属性 / 组合 / 缓动 / 引擎控制 / 压力测试」分类，**点击即播放**
- **中间舞台** — 主卡片、颜色块、不透明度圆、尺寸盒四个动画目标，附网格背景与实时 FPS 浮层
- **右侧面板** — 实时显示当前演示对应的 LAE 流式调用代码，以及彩色运行日志
- **顶栏** — 全局速度滑块 (0.1x ~ 5x)、重播、停止全部、重置舞台

## 环境要求

- .NET 10 SDK
- Windows (WPF)

## 许可

见 [LICENSE.txt](LICENSE.txt)
