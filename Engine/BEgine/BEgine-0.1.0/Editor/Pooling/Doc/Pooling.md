# 对象池与集合池

启用 `com.bengine.pooling`，脚本引用 `BEngine.Pooling`。类型命名空间仍为 `BEngine`，旧的 Get、Release、Clear、Dispose 和五参数构造调用可重新编译后继续使用。

```csharp
using BEngine;

using var pool = new ObjectPool<MyObject>(
    create: () => new MyObject(),
    onGet: item => item.Activate(),
    onRelease: item => item.Reset(),
    onDestroy: item => item.Dispose(),
    maxSize: 32, maxActive: 64);

pool.Prewarm(16);
using (var rental = pool.Rent())
{
    rental.Value.DoWork();
}
pool.Trim(8);
```

| API | 行为 |
| --- | --- |
| Get / TryGet | 借出对象；TryGet 只在达到 MaxActive 时返回 false，其他错误正常抛出 |
| Release / TryRelease | 归还对象；TryRelease 对空值、外来对象、重复归还返回 false，回调错误仍抛出 |
| Rent | 返回 IDisposable 租借对象，using 结束自动归还；重复 Dispose 无副作用 |
| Prewarm(n) | 将空闲对象补足到 n，返回新增数量；调用 onRelease 初始化空闲状态，不调用 onGet |
| Trim(n) | 将空闲缓存裁剪到最多 n，返回移除数量；不影响已借出的对象 |
| Clear | 清空空闲缓存 |
| Dispose | 停止借出并清空缓存；已借出的对象仍有效，后续归还时销毁 |
| Owns / IsActive | 按引用身份查询归属与借出状态 |
| Statistics | 当前数量、峰值借出量，以及累计创建、成功借出、复用、成功归还和销毁尝试次数 |

`maxSize` 限制空闲缓存，`maxActive` 限制同时借出数量，二者独立；`defaultCapacity` 只预分配内部存储，不创建对象。Prewarm/Trim 的目标必须位于 0 到 MaxSize 之间。预热不计入成功归还次数。

工厂不得返回空值或本池已经拥有的对象。获取或归还回调失败时，该对象从池中移除并尝试销毁，避免复用部分初始化的数据；如果销毁也失败，两个异常会一起报告。批量清理会继续处理剩余对象，最后汇总销毁异常。回调可读取统计，但不能递归修改同一个池。

租借带有代次检查：即使对象被手动归还并再次借出，旧租借的 Dispose 也不会回收新的使用者；旧租借的 Value 会拒绝访问。原始 Get/Release 返回的对象不携带代次，请勿在归还后继续使用或再次归还旧引用。

所有池均为实例对象，供单线程使用，没有全局静态缓存。游戏对象池请在引擎主线程使用。Rent 每次创建一个引用类型包装；对分配敏感的循环可使用 Get/Release，并用 try/finally 确保归还。

## 集合池

```csharp
using var lists = new ListPool<int>(maxSize: 8, maxRetainedCapacity: 1024);
using (var rental = lists.Rent())
{
    rental.Value.Add(42);
}
// 归还时清空内容，超过 maxRetainedCapacity 的 List 会释放大数组。

using var maps = new DictionaryPool<string, int>(StringComparer.OrdinalIgnoreCase);
using var sets = new HashSetPool<string>(StringComparer.OrdinalIgnoreCase);
```

HashSetPool/DictionaryPool 保留指定的比较器及集合容量，归还时清空元素。通用 `CollectionPool<TCollection,TItem>` 可通过工厂支持其他实现 ICollection 的集合。销毁普通托管对象时，没有 onDestroy 回调也会从池中解除引用；原生资源应提供明确的销毁回调。
