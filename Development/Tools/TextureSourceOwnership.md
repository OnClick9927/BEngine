# 纹理输入与解码职责

Graphics 提供不创建 GPU 对象的 `TextureSourceReader`，统一导入产物、物理路径和
Resources 字节读取。路径优先级保持为导入产物、绝对路径、当前目录、工程 Assets/
dataPath 和逻辑资源。SceneTextureCache 保留 GUID 资源解析、采样元数据和准备调度；
TiledMap 的适配器只提供瓦片纹理入口及名称，不再实现通用路径规则。

`TextureImageData.Decode(byte[])` / `Decode(ReadOnlySpan<byte>)` 统一 BTEX 容器和图片
解码，容器校验和、mip 长度验证保持有效。解码器新建的 RGBA 数组在 Graphics 内转移
所有权，避免复制整张图片；公开构造器仍防御性复制外部数组。

`HasOpaqueAlpha()` 检查所有 RGBA mip；压缩块保持保守透明策略。由于公开 mip 字节
允许修改，结果不缓存。按索引扫描避免枚举器分配。TiledMap 继续拥有瓦片缓存失效、
点采样、缺失棋盘图、瓦片绘制和排序，Graphics 不依赖 TiledMap 或场景渲染包。

Graphics 通过 RuntimeAssemblyCleanup 显式退役导入解析回调。Core 已有针对被索引宿主
类型的静态委托清理；显式清理进一步覆盖未进入该索引的导入扩展，不依赖反射兜底。
没有把内部可写委托公开为开发者 API。

验证命令为 `Development/Tools/Test-TextureSourceOwnership.ps1`，输出必须指向工作区 Temp。
独立进程覆盖路径优先级、虚拟资源防御性复制、PNG/BTEX、mip 透明度、损坏容器、
10000 次预热零分配透明度查询以及未索引可卸载回调清理。Parity 下的相同程序分别
对旧/新安装程序集运行，核对混合瓦片的纹理上传、顶点、混合状态和绘制记录。
记录型 RHI 的一致性不等同于 GPU 像素测试或完整 Player 性能结论。
