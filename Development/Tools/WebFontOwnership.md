# Web 字体的平台边界

UIElements 保留字体链、HarfBuzz OpenType shaping、字形簇、双向文本、回退、换行、
布局缓存及文本图片策略。FreeType 原生句柄、字体复制、字符覆盖查询、度量及字形像素
写入由 Platform.Web 提供；UIElements 不再直接声明字体 P/Invoke。

`IPlatformFontRasterizer` / `IPlatformFontFace` 位于 Platform.Contracts。
接口只使用字节、字形 ID、交错 x/y 坐标跨度和 RGBA8 输出跨度，不引用 UI 类型或包。
OpenFace 同步复制输入，返回值独立拥有字体；DrawRun 同步借用调用方的缓冲区。
字体在所属线程上使用和释放，不允许调用方在并发使用中释放。
原生写入前检查坐标数量、字形数量、尺寸和目标缓冲区长度，避免越界访问。

Platform.Web 的 `WebFontRasterizer` 调用原有 `libBEngineWebFont` 五个入口。
原生 C 和静态库没有改变，字体格式、色彩支持和 HarfBuzz scale 512 规则保持原状。
度量通过栈上三个 float 返回，移除原 UI 后端每次查询创建的 float[3]。
绘制直接借用 glyph/point/pixel 缓冲，不新增数组转换或每帧服务扫描。

只有选择 `webFontBackend: FreeType` 的共享 Player 文本宿主调用 Register。
它提供默认能力和服务容器贡献，不创建字体句柄。默认 Skia 宿主不启用该服务；
未使用的适配器可以裁剪掉，默认模式无需链接 libBEngineWebFont。
Core、平台及共享宿主不引用 UIElements；其他消费者可以直接使用中立字体接口。

Development/Tools/Tests/WebFontOwnership 编译真实 UIElements 的条件配置，并在真实 Edge/WASM
上将旧后端夹具与新实现对照：字形、度量、OpenType、中文、RTL、组合字符、无效
UTF-16、回退、换行、裁剪、颜色和最终像素。另有独立消费者、失败清理、释放及
分配检查。DefaultWeb 在没有 FreeType 静态库的情况下验证默认 Skia 的排版和绘制。
测试的字体副本和浏览器配置只位于 Temp，不随引擎分发。

本轮 Edge 原生链接 WASM 测试通过 294 项，默认 Skia 通过 9 项。固定局部结构
避免了 Web 解释器的动态 localloc 路径；热身后 3000 次度量的实测分配为新路径
0 字节、旧路径 96000 字节。该微测不代表完整游戏的帧率或内存变化，也不是完整
Example/Snake AOT 发布验收。实际 Editor 的 41 项工作流和启动/重载探针通过。

本轮仅迁移原生字体能力，不改变编辑器字体后端，不声称提升整体游戏 FPS。
验证记录位于 Temp/Engine/Verification/WebFontOwnership_20261114。
完整 Example/Snake Player 与全部包的语义审查仍需独立验收。
