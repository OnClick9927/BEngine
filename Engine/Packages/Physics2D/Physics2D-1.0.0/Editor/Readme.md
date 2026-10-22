# BEngine Physics2D

孔洞/布尔运算、质量与惯性、双接触点求解及边界说明见 [多边形与刚体求解](Doc/GeometryAndDynamics.md)。

详细的生命周期、分包更新、平台扩展、行为限制与验证步骤见 [运行与扩展指南](Doc/RuntimeGuide.md)。任务 Skills 位于 `Editor/Skills`。

Physics2D 为 BEngine 提供单线程、Fix64、场景隔离的 2D 物理。包包含 Rigidbody2D、Box/Circle/
Capsule/Polygon Collider、PhysicsMaterial2D、碰撞与 Trigger 消息、Raycast/Overlap/Cast 查询。
它不包含任何 3D API，也不会启动运行时工作线程。

## 快速开始

1. 打开 Window > General > Package Manager，启用 2D Physics。
2. 在 Examples 中导入 Rigidbody And Queries。
3. 打开 Assets/Examples/RigidbodyAndQueries/res/Physics2D.scene.yaml。
4. 选择 Physics2D Body，检查 SpriteRenderer、BoxCollider2D、Rigidbody2D 和脚本。
5. 点击 Play，观察重力、摩擦、弹性与连续碰撞；按 Space 重置并施加速度。
6. 在 Console 查看向下 Raycast 与 OnCollisionEnter2D 的接触信息。
7. 停止 Play；速度、碰撞缓存和运行时组件值会随镜像销毁，场景资源保持不变。

## 完整示例

- Rigidbody And Queries：场景直接保存 Dynamic Body、BoxCollider、Bouncy Material、Floor 和
  Camera2D。展示 AddForce、Continuous、Raycast 与 Collision contacts。
- Triggers And Queries：场景直接保存 Trigger Area、Kinematic Probe、Circle Targets 和 Camera2D。
  展示 Enter/Stay/Exit、OverlapCircle、OverlapBox 与 CircleCast。

每个示例包含独立 asmdef、运行时脚本、详细 Readme 和可直接检查的场景，不会依赖一个空
Bootstrap 在 Play 时创建所有主体。Package Manager 可逐个 Import/Reimport。

## 组件介绍

Rigidbody2D 字段包括 mass、gravityScale、linearDamping、angularDamping、bodyType、simulated、
linearVelocity、angularVelocity、constraints、collisionDetectionMode 和 interpolation。
Dynamic 受力与重力影响；Kinematic 用 MovePosition/MoveRotation 驱动；Static 作为固定几何。

所有 Collider 共享 isTrigger、offset、density、sharedMaterial/material，并提供 attachedRigidbody、
bounds、ClosestPoint、OverlapPoint、Distance 与 Raycast。attachedRigidbody 会向父级查找，支持常见的
“父对象 Rigidbody2D + 子对象 Collider2D”结构。Box 使用 size，Circle 使用 radius，Capsule 使用
size/direction；Polygon points 按顺序定义简单多边形，凹形会确定性地分解为凸形。PhysicsMaterial2D 的 friction 与
bounciness 控制摩擦和反弹。

## 编辑器操作

使用 Add Component 的 AdvancedDropdown 展开 Physics 2D 子层添加组件。Scene Gizmos 默认在
选中时显示 Collider 轮廓；可以在 Gizmos 类型菜单单独关闭 BoxCollider2D、CircleCollider2D
等外部类型。先确定 Transform 与 Sprite 尺寸，再匹配 Collider；需要运动才添加 Rigidbody。
区域检测勾选 isTrigger，并在 MonoBehaviour 实现 Trigger 回调。

## 运行时 API

- Rigidbody2D.AddForce、AddTorque、AddForceAtPosition
- Rigidbody2D.MovePosition、MoveRotation、Sleep、WakeUp
- Physics2D.Raycast、RaycastAll、CircleCast
- Physics2D.OverlapPoint、OverlapCircle、CheckCircle、OverlapBox、OverlapBoxAll、CheckBox
- Physics2D.Distance、IgnoreCollision、IgnoreLayerCollision、GetIgnoreCollision、SyncTransforms
- Physics2D.Simulate（只在关闭 autoSimulation 后，以固定正步长调用）

Raycast/Overlap/Cast 使用 Circle 与凸 Polygon 的定点窄相位；OverlapBoxAll 支持旋转角度，CircleCast
使用边和圆角的解析扫掠。查询支持引擎 Layer Mask 和 QueryTriggerInteraction，碰撞矩阵直接使用
GameObject.layer。RaycastHit2D 提供 collider、point、normal、distance、fraction、rigidbody、transform。
示例：

    using World = BEngine.Physics2D.Physics2D;

    World.SyncTransforms();
    var grounded = World.CircleCast(transform.position, 0.4, Vector2.down,
        out var hit, 0.8, groundMask, QueryTriggerInteraction.Ignore);

## 消息回调

非 Trigger 接触使用 OnCollisionEnter2D、OnCollisionStay2D、OnCollisionExit2D，参数为 Collision2D。
Trigger 使用 OnTriggerEnter2D、OnTriggerStay2D、OnTriggerExit2D，参数为 Collider2D。
Collision2D 提供 relativeVelocity、contacts、contactCount 和 GetContact。

## 如何扩展

项目 Runtime asmdef 引用 BEngine.Physics2D。角色控制器应在 Update 采集输入，在 FixedUpdate
施力；缓存 Rigidbody/Collider；用 CircleCast + Layer Mask 判断地面；用 constraints 锁轴，
不要每帧覆盖解算结果。自定义碰撞行为可组合现有 Collider，而不是复制物理世界实现。

编辑器扩展放在 Editor asmdef。可用 MenuItem 在 Tools/项目名 下添加批量材质或 Layer 设置，
用 OnDrawGizmosSelected 绘制自定义 Query 范围。外部组件会自动出现在 Scene Gizmos 类型列表。
运行时 Scene 与组件禁止保存到 asset；需要持久化的设置只能在非 Play 状态修改。

## 排错

- 穿透：使用 Continuous，检查 fixedDeltaTime、Collider 尺寸和单步位移。
- 无回调：检查 Collider enabled、Layer、simulated，以及 Trigger/Collision 签名。
- Query 查不到：先 SyncTransforms，检查 Layer Mask 与 Trigger policy。
- 运动不稳定：不要在 Update 以可变步长 Simulate；避免同时改 Transform 和 Dynamic Rigidbody。
- 停止 Play 后状态消失：这是编辑器运行镜像隔离的预期行为。

完整字段表、图文工作流、API、回调与扩展教程见 Editor/Doc/index.html。

## 新增示例

示例工程 Main → Overlap & Trigger Filtering：圆形查询、触发器包含/忽略、改变查询半径。

## 编辑器工作流

Collider2D Inspector 提供 Fit to Sprite，根据 Sprite 尺寸与 Pivot 适配 Box/Circle 碰撞体，支持撤销。Rigidbody2D 在运行时可查看状态并 Sleep/Wake。

## 组合几何、关节与效应器

- `PolygonCollider2D` 保留凹口，支持顺时针或逆时针顶点；去除重复/共线边，自相交输入会报错，单路径最多 4096 点。局部形状分解缓存随 points 修改失效，移动和旋转不重复三角化。
- `CompositeCollider2D.paths` 表示多个实心路径；子碰撞体启用 `usedByComposite` 后归入最近的组合碰撞体，不重复参与世界查询。当前是形状并集，不做布尔轮廓合并或孔洞相减。
- `EdgeCollider2D` 使用连续线段和正数 edgeRadius，端点以延伸的矩形条近似，不是圆形端帽。
- `DistanceJoint2D`、`SpringJoint2D`、`HingeJoint2D`、`FixedJoint2D`、`SliderJoint2D` 支持 connectedBody、局部锚点、世界锚点（无连接刚体）、碰撞开关和 breakForce。断开时向同对象行为发送 `OnJointBreak2D(Joint2D)`。铰链支持角度限制和电机，滑动关节支持位移限制。
- `PlatformEffector2D` 按表面朝向筛选单向接触；`AreaEffector2D` 施加区域力；`BuoyancyEffector2D` 按浸没包围盒面积近似浮力和阻力。碰撞体需勾选 `usedByEffector`，区域力/浮力通常与 isTrigger 配合。
- `com.bengine.tilemap-physics2d` 独立桥接包提供 `TilemapCollider2D`，将 TiledMap 的 HasCollider 标记转为物理路径。只启用 TiledMap 不会强制依赖 Physics2D。

当前求解仍是轻量定点实现：每个碰撞体对使用一个接触流形，关节角度采用现有刚体质量近似转动惯量，不等同于 Box2D 的完整多点约束求解。连续碰撞仍为原有扫掠实现，不保证高速物体与所有新增约束组合的稳定性。复合几何、边缘与关节支持选中时的 Scene Gizmo。
