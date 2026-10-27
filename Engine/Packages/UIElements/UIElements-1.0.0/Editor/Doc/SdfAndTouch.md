# SDF 字体和运行时触摸

在 `Window/UI Toolkit/SDF Font` 打开字体窗口，输入本机已安装的字体族名称和需要烘焙的字符。默认包含基本拉丁字符；中文及其他语言应加入实际文本使用的字符。采样尺寸为 16–256，距离范围为 2–32。点击 Bake and Save 保存到工程 Assets 内的 `.sdfont`。双击字体资产可以重新打开，Rebake 更新原文件；创建和重烘焙支持文件 Undo/Redo，Play 中禁止写入。

`.sdfont` v2 保存预烘焙字形、源字体和有序 fallback 字体；Player 不依赖目标系统已安装字体。HarfBuzz 执行 OpenType GSUB/GPOS shaping，布局按 Unicode 字素选择 fallback 并组织双向运行，支持组合字符、阿拉伯/天城文/希伯来混排、换行和按词换行。缺失字形可从内嵌字体按需栅格化并缓存；彩色 Emoji/彩色字体使用 RGBA 图集，普通轮廓继续使用单通道 SDF 和描边。旧 v1 资产仍可读取，但没有内嵌源字体时无法动态补字。

Player 构建会把内置 AOT 资源中的 `.sdfont` 预编译为二进制运行时数据，省去启动时的 JSON 解析和 Base64 转换。原项目字体、编辑器保存和克隆格式保持 JSON，资源地址、GUID、字形指标、图集像素、内嵌字体及 fallback 顺序全部保留。构建缓存包含运行时字体格式版本；编译前验证原资源快照，归档再记录编译后数据的大小和 SHA-256。

`SdfFontAsset.FromBytes` 同时读取 JSON 和二进制字体；自定义构建流程可调用 `SdfFontAsset.CompileRuntimeBytes`。二进制封装版本独立于字体数据的 v1/v2，使用固定小端数值和原始 IEEE 浮点位，检查长度、UTF-8、数量、版本与尾部数据，并复用字体原有的合法性验证。

在 UI Builder 的 Text 区填写 SDF Font Asset（例如 `Assets/Fonts/UI.sdfont`），设置 Font Size、Outline Width 和 Outline Color。也可以使用样式表：

```css
.dialog { font-asset: url('Assets/Fonts/UI.sdfont'); font-size: 32px; }
.title { text-outline-width: 2px; text-outline-color: #003366ff; }
```

字体路径从父元素继承；使用工程 `Assets/...` 路径、资源路径或资产 GUID。仅使用到的资产打包时，也会收集样式中引用的 `.sdfont`。字体重新导入会刷新现有渲染器中的图集。字体选择、描边和字号支持 UXML/YAML 保存以及 UI Builder Undo/Redo。SDF 与普通图片、普通文字可以在同一文档中混合。

运行时 `UIDocument` 按触点独立发送 PointerDownEvent、PointerMoveEvent、PointerUpEvent、PointerCancelEvent 和 PointerCaptureLostEvent。事件的 `pointerId` 为触点 ID，`pointerType` 为 Touch；鼠标使用 -1。`position` 为视口左上原点的 UI 像素，`Input.touches` 仍为左下原点游戏像素。文档排序决定最上层命中目标，两个手指可以同时操作不同按钮或滑块。

附着到运行时 UIDocument 的自定义控件可以在 PointerDownEvent 中调用 `CapturePointer(evt.pointerId)`，在视口外继续接收移动和释放；使用 ReleasePointer/HasPointerCapture 管理捕获。控件移除/禁用、文档禁用、触点取消/丢失及应用失焦均释放捕获。触摸拖动 ScrollView 超过阈值后开始滚动，并抑制内部按钮点击。

Windows 编辑器 Game View 转发原生 WM_TOUCH 的独立触点、位置、取消状态，支持编辑器缩放并过滤系统提升的重复鼠标事件。编辑器外壳继续使用系统提升的主触点鼠标事件。Windows 的 WM_TOUCH 后端提供接触状态压力 0/1，不提供笔压曲线。

发行宿主显式引用 SkiaSharp/HarfBuzzSharp 的目标平台依赖；Linux 带原生 `.so`，Android/iOS 使用平台程序集，Web 静态链接 WebAssembly native assets 并需要 `wasm-tools`。`BuildHosts/Shared/PlayerTextHost.cs` 在启动时注册宿主文字程序集，热更新包复用目标平台绑定；桌面宿主同时注册原生导入解析，以兼容发行目录的小写文件名。自定义宿主需保留这些引用、注册和解析。

已验证复杂文字布局、fallback、动态字形和彩色字形的 Vulkan/OpenGL 像素读回，以及 Windows 原生触摸注入、场景回放和既有独立 Player 内容更新。Web 浏览器原生检查实际加载内嵌字体、执行 HarfBuzz 塑形并用 Skia 栅格化，得到 13 字形/1481 个有墨像素。Android APK 包含 Skia/HarfBuzz 原生库，Linux 发布产物包含两个 `.so`；这不替代移动设备和完整 Web UIDocument 的画面/触摸验收。
