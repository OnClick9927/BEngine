# 资源格式与内容存储边界

AB 管理目录、地址、校验和、字节读取及租约。Graphics 管理 Texture、Material、Shader、Font 的资源解码；Rendering2D 管理 Sprite 及其导入参数。AB Runtime 不再引用这两个包，也不判断这些资源类型或固定子资源编号。

Core 的 `RuntimeResourceMetadata` 描述存储无关的地址、虚拟来源、所有者、GUID、子资源编号及导入设置。`RuntimeResourceCodec` 提供具体对象格式策略，由所属程序集在初始化时调用 `RuntimeResourceCodecs.Register` 注册。它不提供包枚举或存储实现，也不触发 GPU 初始化。注册及解码在引擎所属线程上执行。

`SyntheticLocalIdentifier` 为可选的稳定子资源标识：该子对象使用所有者的字节，不要求目录中有单独的数据项。格式包同时判断导入元数据，避免所有同编号的引用都被当作有效资源。旧目录存在相同子项记录时仍使用所有者数据。普通带独立字节的子资源继续保留自己的数据及 GUID。

异步加载先获得内容租约，再在所属线程调用解码器；支持预处理的原有 `RuntimeAssetCodecRegistry` 路径继续使用。整个解码和身份绑定结束后释放租约。等待期间解码器被替换或卸载时终止解码；创建对象后发生取消或校验失败会释放该对象。纹理异步加载直接使用已读取的字节，不再次同步读取。

普通资产的格式回退设置 `PreferAssetDecoder`，保留开发者注册的字节解码器及 `RegisterPrepared` 的优先级。需要完整目录元数据的纹理和 Sprite 保持其原有专用路径优先策略。

资源提供者以规范化对象身份缓存：资源路径、GUID 以及所有者支持的子资源引用共享同一实例。并发加载只保留一个实例，释放重复创建的对象。内置资源归档的对象随其所属域释放。通用清理 `BAsset.ClearLoadedAssets()` 通过 `RuntimeObjectCodec.ClearReferences()` 让格式包失效自己的引用缓存，内容包不直接操作渲染缓存。

程序集卸载沿用 `RuntimeAssemblyCleanup` 及原有资产注册清理路径，同时移除新解码器及子资源索引。Rendering2D 通过自己的 `Editor/tooling.json` 声明发行模块身份，不依靠 AB 间接把场景渲染器带入 Player；平台及 Core 不添加具体包引用。

## API 迁移

- `AssetBundleAssetLoader.LoadTexture(manager, path)` → `LoadAsset(manager, path, typeof(Texture)) as Texture`。
- `AssetBundleAssetLoader.LoadSprite(manager, path)` → `LoadObject(manager, path, typeof(Sprite)) as Sprite`。
- `LoadAssetAsync` 保留；非 BAsset 对象使用新增的 `LoadObjectAsync`。
- 资源提供者的 `TryLoadObject` / `LoadAsync` 不再限定 Sprite，第三方对象格式也可以注册。

场景、Prefab、目录格式、资源 GUID、Importer 设置及工程设置没有迁移要求。程序集依赖变化需重新编译。AB Editor 中既有图形资源收集与编辑器适配仍显式声明图形依赖，本轮仅解耦 Runtime 格式解码。

## 验证

`Test-ResourceCodecBoundary.ps1` 分别运行不引用图形包的测试和图形资源测试。覆盖真实 AB、虚拟 AB、压缩内置归档、自定义子资源、纹理采样参数、Sprite 身份、Material/Shader/Font、缓存别名、取消、程序集卸载与字节租约。内部归档适配使用反射测试，不扩大生产 API 或添加友元授权。

`Tests/ResourceCodecBoundary/Benchmark` 使用前后版本均有的公开 API，对同一虚拟目录进行纹理构造/释放及冷异步读取对照；它是局部微基准，不能代替整局帧率和启动时间。验证产物及测量结果放在仓库外层 Temp。
