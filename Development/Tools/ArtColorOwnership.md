# ArtImport2D 与平台 ICC 边界

ArtImport2D 保留 PSD/PSB 格式解析、图层身份、原始 ICC 元数据、16/32 位通道、
Photoshop CMYK 反相规则、无配置文件的 CMYK 回退及 alpha 处理。
它通过 Platform.Editor.Contracts 的 IEditorColorManagementHost 请求独立转换租约，
不再声明 mscms.dll 导入。Platform.DotNet.Editor 提供 Windows CMM，既可在无窗口
导入工具使用，也可用于默认编辑器；TryAdd 保留显式宿主替换。

原来的 COLOR 声明固定为 8 字节，但 Windows SDK 的 COLOR 联合体含指针对齐成员，
x64 为 16 字节，x86 为 8 字节。新平台实现遵守该 ABI，避免 RGB/gray 多像素错位
及大图越界。不能把旧的错误像素输出当作兼容标准。测试使用独立 MSVC/Windows SDK
程序调用同一 CMM 作为预期值；真实灰度、sRGB、CMYK 配置文件分别验证。
ImporterVersion 从 4 提升为 5，使旧导入缓存失效，但不更改 GUID、图层 local ID 或格式。

每个租约复用 256 像素的缓冲；无 ICC 的导入不再预分配原来的两组 4096 色原生数组。
独立消费者保留旧接口的未标记输入/CMYK 输出对照，覆盖块边界和 4097 像素、
8/16/32 位、PSD/PSB、alpha、显式替换和释放。旧 x64 RGB 仅用小样本记录错误，
不以可能越界的旧实现运行大图。实际 Editor 验证生成纹理、重导入、身份及代码重载。

验证源码在 Development/Tools/Tests/ArtColorOwnership，产物和详细结果在工作区
Temp/Engine/Verification/ArtColorOwnership_20261114。本项仅证明此边界迁移；
不是全部包职责审查、完整 Example/Snake Player 或跨平台 ICC 的通过声明。
