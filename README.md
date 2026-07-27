# LAE — Lively Animation Engine

已使用的项目 [PCSM Next](https://github.com/ksayus/PCSMNext)

一个轻量、高性能的 WPF 动画引擎，提供流式 API 构建复杂动画序列，支持并行与串行编排、丰富的缓动函数、以及全局速度控制。

## 特性

- **流式 Builder API** — 链式调用，代码即动画描述
- **并行 & 序列** — 默认并行动画，`Then()` 分隔序列
- **丰富的变换支持** — Move、Scale、Rotate、Skew、Opacity、Width、Height、Color
- **颜色动画** — sRGB 四通道线性插值，自动处理冻结晶刷
- **缓动函数** — Linear、OutCubic、InOutCubic、OutPow(n)、InOutPow(n)
- **全局速度控制** — 实时调速 0.1x ~ 5x
- **冻结/解冻** — 批量设置属性时跳过动画更新
- **命名动画组** — 按名称管理动画生命周期
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

## API 参考

### 构建器入口

```csharp
LA.Builder()          // 匿名动画组
LA.Builder("name")    // 命名动画组, 同名启动时自动停止旧的
```

### 变换动画

| 方法 | 说明 |
|------|------|
| `Move(t, x, y, ms)` | TranslateTransform 移动到绝对坐标 |
| `MoveBy(t, dx, dy, ms)` | TranslateTransform 相对移动 |
| `Scale(t, s, ms)` | ScaleTransform 缩放到绝对倍率 |
| `ScaleBy(t, ds, ms)` | ScaleTransform 相对缩放 |
| `Rotate(t, deg, ms)` | RotateTransform 旋转到绝对角度 |
| `RotateBy(t, ddeg, ms)` | RotateTransform 相对旋转 |
| `Skew(t, deg, ms)` | SkewTransform 偏斜到绝对角度 |
| `SkewBy(t, ddeg, ms)` | SkewTransform 相对偏斜 |

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

// 全局速度
LAEngine.Speed = 2.0;           // 2 倍速播放 (0.1 ~ 200)

// 冻结 / 解冻 (批量设置属性时防止动画干扰)
LAEngine.Freeze();
// ... 批量设置属性 ...
LAEngine.Unfreeze();

// 状态查询
LAEngine.ActiveGroupCount;      // 活跃动画组数量
LAEngine.IsFrozen;              // 是否处于冻结状态
```

### 缓动函数

```csharp
Easing.Linear                   // 匀速
Easing.OutCubic                 // 缓出 (默认)
Easing.InOutCubic               // 缓入缓出
Easing.OutPow(3)                // 自定义指数缓出
Easing.InOutPow(5)              // 自定义指数缓入缓出
```

## 架构设计

```
LA.Builder("name")
  └── LABuilder (流式构建器, 累计动作)
        └── LAGroup (动画组, 管理动作列表)
              ├── DependencyPropertyLA  → 依赖属性动画
              ├── ScaleTransformLA      → 缩放动画
              ├── RotateTransformLA     → 旋转动画
              ├── SkewTransformLA       → 偏斜动画
              ├── ColorLA               → 颜色动画
              ├── WaitLA                → 等待动作
              └── CallbackLA            → 回调动作
LAEngine (全局引擎)
  └── CompositionTarget.Rendering 驱动帧更新
```

**帧驱动**: 动画引擎通过 `CompositionTarget.Rendering` 事件驱动，自动在有动画时订阅、无动画时取消订阅，不消耗额外资源。

**增量更新**: 数值动画采用增量模式（每帧只写 `progress * totalDelta` 的增量），天然支持多个动画同时作用于同一属性。

**序列门**: 每个动作携带 `WaitForPrevious` 标记，引擎在帧更新时按序检查：如果 `Pending` 动作前面还有未完成动作，则整组暂停等待。

## 项目结构

```
LAE/
├── LivelyAnimationEngine.csproj   # 核心引擎库
│   ├── LAEngine.cs                # 全局引擎 (Play/Stop/Freeze/Speed)
│   ├── LAGroup.cs                 # 动画组 (动作管理/帧更新)
│   ├── LABuilder.cs               # 流式构建器 (公开 API)
│   ├── AnimationInterface.cs      # 内部动画实现 (变换/颜色/回调)
│   ├── Easing.cs                  # 缓动函数
│   └── InternalsVisibleTo.cs
├── UI/                            # 全功能测试界面 (38 个测试按钮)
│   ├── UI.csproj
│   ├── MainWindow.xaml
│   └── MainWindow.xaml.cs
```

## 运行

```bash
# 全功能测试界面
dotnet run --project UI/UI.csproj
```

## 环境要求

- .NET 10 SDK
- Windows (WPF)