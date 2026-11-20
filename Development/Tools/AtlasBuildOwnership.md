# 图集发布编译的职责

TextureAtlas Runtime 保留图集数据、BTAR 信封读写、格式校验、资源引用与运行时解码。
是否生成紧凑产物、转换器独占条件、取消、DTO 往返比较和源数据回退属于发布策略，
由 TextureAtlas Editor 的 `TextureAtlasBuildDataCompiler.Compile` 提供。
Core 与 Platform 不增加任何图集类型或包名分支。

编辑器注册使用 Editor 中的编译方法。通用资源构建管线以回调声明程序集为注册所有者；
因此卸载 Editor 程序集会注销该编译器，不必等待 Runtime 程序集卸载。
Runtime 不再包含私有 `CompileRuntimeBytes`；没有删除公开 Runtime API。
BTAR 格式标识 `atlas-envelope-v1`、旧 YAML、持久 GUID、默认字段和自定义转换器回退保持兼容。
底层信封写入属于格式编解码，不等同于发布策略，继续由 Runtime 内部提供给同包 Editor。

`Development/Tools/Test-AtlasBuildOwnership.ps1` 以同一测试程序比较旧/新编译器产物，覆盖取消、
重复编译、损坏输入、额外转换器、实际构建管线注册和仅 Editor 退役。
独立 Runtime 进程消费这些 Editor 生成的产物，并检查未加载 Editor 程序集。
ObjectStateContracts 使用独立 Editor 生产者生成夹具，Runtime 不再反射调用构建实现。
这些契约与字节比较不能替代实际 Player、GPU 或所有平台性能验收。

本机 Unity 2022.3.62f2 的 UnityEditor.CoreModule 中，
`UnityEditor.U2D.SpriteAtlasUtility.PackAtlases` / `PackAllAtlases` 属于 Editor；
这里只参考编辑期打包与运行期消费的边界，不假定两套引擎内部实现相同。

完整 Core 与全部包的语义审查仍需继续，不能用私有引用数量或编译通过代替。
