# Pooling

Pooling 是内置 Core 模块，程序集为 `BEngine.Pooling`，不需要在工程包清单中启用。公开类型保留 `BEngine` 命名空间；使用显式 asmdef 引用的脚本添加 `BEngine.Pooling`。

提供 ObjectPool、自动归还的 PooledObject、CollectionPool、ListPool、HashSetPool 和 DictionaryPool。支持预热、缓存裁剪、同时借出数量限制及统计。

[API 与生命周期说明](Doc/Pooling.md)。`Editor/Examples/PoolingGettingStarted.bpackage` 提供入门示例；主示例工程的 Object Pool & Reuse 场景演示预热、获取、回收、裁剪和集合租借。
