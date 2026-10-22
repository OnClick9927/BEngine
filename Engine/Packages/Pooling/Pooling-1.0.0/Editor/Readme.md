# Pooling

启用 `com.bengine.pooling`，程序集为 `BEngine.Pooling`。公开类型保留 `BEngine` 命名空间；使用显式 asmdef 引用的脚本添加 `BEngine.Pooling`。Core 不再包含对象池实现。本包只依赖 Core，不依赖其他功能包。

提供 ObjectPool、自动归还的 PooledObject、CollectionPool、ListPool、HashSetPool 和 DictionaryPool。支持预热、缓存裁剪、同时借出数量限制及统计。

[API 与生命周期说明](Doc/Pooling.md)。Package Manager 的 Examples 页签可导入 PoolingGettingStarted；主示例工程的 Object Pool & Reuse 场景演示预热、获取、回收、裁剪和集合租借。
