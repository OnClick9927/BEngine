# Graphics 与通用资源机制的边界

Core 提供对象身份、销毁、资源提供方、取消与协作式加载机制。Graphics 提供纹理、材质、Shader、图片解码、上传和 GPU 缓存；这些实现没有迁回 Core。Graphics Runtime 已可关闭包私有访问权限编译，Core 的历史全包 friend 授权尚未整体移除。

## 调用约定

- `RuntimeAssetReferences.TryReadMetadata` 查询当前 `IAssetReferenceResolver`。Core 不读取 `.meta` 文件，也不解释纹理导入设置；导入设置由 Graphics 解释。
- `Resources.LoadAsync(string, Type, AsyncLoadContext)` 在调用者现有上下文中加载，保留取消、优先级、进度和协作预算。资源地址仍必须相对于资源根；读取物理资源时先注册根，不接受绝对资源地址。原有返回 `ResourceRequest` 的重载不变。
- Graphics 使用已有 `BAsset.cacheGeneration`、`RuntimeAssetReferences.TryGetSubAssetIdentity`、`BAsset.Load` 和 `RuntimeAssetDecoding.DecodeText`，不再访问私有加载器、私有身份字段及私有文本解码方法。
- `RenderTexture.Dispose` 通过 `BObject.DestroyImmediate` 进入统一生命周期，资源卸载、对象注销、销毁回调和 GPU 释放仍各执行一次。

没有修改资源 GUID、材质 YAML、子资源引用格式或包清单，也没有增加包之间的实际依赖。材质和 Shader 的 BOM 解码规则沿用 Core 通用文本解码。

## 验证

`Development/Tools/Test-GraphicsResourceContracts.ps1` 使用无 Core friend 授权的独立消费者和记录 RHI。覆盖元数据替换、物理和逻辑资源、上下文与取消、失败与错误类型、可读纹理、材质子资源引用、GPU 准备与失效、RenderTexture 的一次性销毁，以及五种 BOM 编码。

帧缓存测量只覆盖预热后的帧作用域及纹理命中，不代表整个编辑器或 Player 的性能。记录 RHI 验证资源协议和释放次数，实际 GPU/平台行为仍由 Example 与 Snake 的 Player 回归补充。

完整隔离编译当前继续暴露 Rendering2D 和 UIElements 的历史私有访问；编译器诊断数量并非整仓剩余问题总数。整个职责审计仍需继续，不能把本阶段通过当成所有包已完成清理。
