# 定点数与运行一致性

BEngine 的逻辑数值使用 `Fix64`。它以有符号 Q32.32 格式保存数值：底层为 64 位整数，其中 32 位为小数部分。`Vector2`、`Vector4`、`Color`、`Rect`、Transform、游戏时间、动画曲线、物理状态和寻路代价采用定点表示。

## 编写逻辑

需要小数常量时，直接从十进制文本或 decimal 创建，避免先经过浮点运算：

```csharp
Fix64 speed = Fix64.Parse("0.1");
Fix64 damping = Fix64.FromDecimal(0.25m);
Vector2 offset = new(speed, damping);
Fix64 angle = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg;
```

`Fix64.FromRaw` 用于保存或恢复已经量化的原始整数，不是从普通整数创建值的方法。浮点转入定点仅用于外部接口：转换按 IEEE 位模式执行整数缩放并向零截断；NaN、Infinity 和超出定点范围的值会抛出异常。

从平台计时进入逻辑时使用 `Fix64.FromTimeSpan(elapsed)`，由 ticks 通过整数计算得到秒数，避免 `TotalSeconds` 中转。固定步长应直接保存为 `Fix64`，不要每帧重新计算浮点步长。

YAML 数值直接保存为定点的精确十进制文本。旧的小数 scalar 和早期 `rawValue` 映射仍可读取。Sprite 锚点、图集参数、Tile UV、碰撞顶点、Tile 动画时长以及 ArtDocument slice 坐标保存为定点数据，不在序列化时转成 float/double。

## UI 样式

UI 长度、布局因子、CSS 百分比、相对单位和布局计算使用 `Fix64`。位置偏移和最大尺寸为可空定点数；`null` 显式表示自动定位或没有上限：

```csharp
element.style.width = Fix64.Parse("120.5");
element.style.flexGrow = Fix64.One;
element.style.left = null;      // auto
element.style.maxWidth = null;  // 无上限
element.style.SetPadding(Fix64.Parse("4.25"));
```

原先赋给样式属性的浮点字面量需要改为整数、`Fix64.Parse` 或 `Fix64.FromDecimal`。`FloatField` 等用于编辑外部浮点数据的控件仍可处理对应类型；字体栅格化和 GPU 提交在接口边界转换为平台所需格式。

## 一致性条件

定点运算能避免逻辑计算依赖处理器浮点实现，但一致的模拟还需要以下条件：

- 每个逻辑步输入相同、步长相同，事件在相同逻辑步应用。
- 使用相同随机种子及调用顺序，需要回放时保存和恢复 `Random.state`。
- 对象创建、系统执行、碰撞求解和集合遍历的顺序稳定。运行时随机生成的 GUID 只能作为身份，不用于决定物理求解或同距查询的顺序。
- 后台任务和网络结果在指定的逻辑步提交，避免完成时机直接影响模拟。
- 不将平台时钟、字体测量、音频设备或视频解码的结果直接作为同步逻辑的权威输入。

渲染设备的顶点和 shader、字体栅格化、音频 PCM/DSP、原生视频解码以及性能统计仍有浮点接口。不同 GPU、驱动、字体和媒体解码器可能产生像素或采样差异；逻辑一致性应通过各逻辑步的状态数据校验，而不能依赖截图逐像素相同。
