# 编辑器共享图片源与 GUI 呈现的职责

`IGpuCanvasResourceResolver`、`CanvasResourceUtility` 和 `FileGpuCanvasResourceResolver`
由 EditorServices 拥有。它们提供中立的图片数据、编辑器/运行资源根查找、预览版本后缀处理和
可替换的图片源服务；不创建窗口、GPU 或 GUI 控件。文件图片的解码继续通过平台的
`EncodedImages` 能力，默认图形编解码器由 Graphics 贡献，没有在共享服务中重复实现。

EditorServices 通过现有服务贡献机制注册默认源和门面。宿主显式提供的源保持优先，贡献随
所属程序集退休。IMGUI 只保留 GPU 画布、文本和控件呈现适配，UIElements 直接消费共享服务，
不再绕过边界调用 IMGUI 的内部图片实现。UIElements 的运行时及其额外文件回退没有改变。

已有 `TryResolveTexture` 保持直接访问后端的语义。新增
`TryResolvePresentationTexture` 先尝试现有 instrumentation 贡献，再调用后端；两个编辑器
UI 都使用它，保留事件顺序、首个成功即停止及源像素/透明度。这里不增加永久图片缓存，
每个呈现器原有的缓存和失效策略继续由该呈现器拥有。

三个公开类型保留命名空间和方法；IMGUI 提供到 EditorServices 的包间类型转发，兼容已经编译
的扩展。Core 和 Platform 没有新增包名、包引用或类型转发。包清单及实际依赖不变；两种 UI
原来都已经直接依赖 EditorServices。

验证入口为 `Development/Tools/Test-EditorImageResourceOwnership.ps1`。独立消费者不引用或加载
IMGUI、UIElements、Graphics、EditorWorkbench，覆盖默认/显式服务、资源根、版本后缀、
缺失编解码器、覆盖顺序、作用域恢复、所属贡献退休及暖态分配。旧消费者先用迁移前 DLL
编译，再原样在新程序集上运行。另用真实 PNG 和实际 Editor 的启动/重载验证两种 UI。

这不是完整的所有包职责清理结论。其他跨包内部调用需要按数据与生命周期逐项判断，
不能把减少 friend 调用数量或依赖图无环当成全仓语义验收。
