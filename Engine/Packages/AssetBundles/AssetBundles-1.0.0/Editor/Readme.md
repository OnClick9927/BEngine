# AssetBundles

### AssetBundle Package 与增量构建

`assetbundle package` 是构建时的资源分组，一个包可以包含多个 Bundle。
例如把字体、图片、配置分别归入 `fonts`、`images`、`config`：只修改配置后，
构建会重新生成有变化的配置 Bundle，字体和图片直接复用已验证的压缩产物。
仍会扫描、校验输入并生成完整发布清单；复用不是完全跳过资源检查。

编辑器菜单 **Assets → AssetBundle Packages...** 可配置、保存并构建；
**Project Settings → Asset Bundles → Configure AssetBundle Packages...** 也可打开。
项目配置位于 `ProjectSettings/AssetBundlePackages.yaml`，只在编辑器构建时读取：

```yaml
format: BEngine.AssetBundlePackages
version: 1
defaultPackageName: default
packages:
  - name: fonts
    assetPaths: [Assets/GameContent/Fonts]
  - name: images
    assetPaths: [Assets/GameContent/Images]
  - name: config
    assetPaths: [Assets/GameContent/Config]
```

规则可以指定目录或单个资源，最长路径匹配优先；相同路径不能属于两个包。
未匹配的资源进入 `defaultPackageName`。`bengine` 为自动生成的程序集和引擎资源保留。
资源原有 `.meta` 中的 `assetBundleName` / `assetBundleVariant` 决定包内 Bundle 分组，
未指定名称的资源使用构建入口的默认 Bundle。存在包规则时全局 Bundle 名为
`包名--包内Bundle名`，资源 address 保持不变。
没有配置时保留原有 Bundle 名，并归入 `default` 包。
Player 内容构建继续遵循 AOT / Resources 与热更资源的原有边界。

首次使用 **Build All Packages** 建立基线，之后可继续全部增量构建，或勾选包后使用
**Build Selected Packages**。选包构建仍检查完整发布输入：未选包如果有修改、缓存缺失
或缓存损坏，会报错并要求把对应包纳入构建；不会静默发布旧内容。
**Force Rebuild Selected** 会重新压缩本次选择的包；全部构建时作用于所有包。

默认构建缓存位于 `Library/AssetBundleBuildCache/<发布标识>`，与临时 Player staging 分离。
输入指纹包含资产内容、导入器设置、依赖关系、平台、压缩、加密以及构建器格式；
复用前检查大小和 SHA-256，缓存损坏会重建选中包。删除 Library 后需要重新 Build All。
结果的 `BuiltBundleCount`、`ReusedBundleCount` 和 `Packages` 可用于记录每包实际构建量。

脚本可使用强类型包定义：

```csharp
AssetBundleBuildResult result = await AssetBundleBuilder.BuildPackagesAsync(
    workspace, database,
    [
        new AssetBundlePackageBuildDefinition
        {
            Name = "fonts",
            Bundles = [new() { Name = "main", AssetPaths = ["Assets/GameContent/Fonts"] }]
        },
        new AssetBundlePackageBuildDefinition
        {
            Name = "images",
            Bundles = [new() { Name = "main", AssetPaths = ["Assets/GameContent/Images"] }]
        },
        new AssetBundlePackageBuildDefinition
        {
            Name = "config",
            Bundles = [new() { Name = "main", AssetPaths = ["Assets/GameContent/Config"] }]
        }
    ],
    new AssetBundleBuildOptions
    {
        PackageName = "my-game", // 整个发布流的标识，与上述逻辑分包名不同
        Version = "v2",
        OutputDirectory = outputDirectory,
        // 首次设为 null 构建全部；有完整基线后可仅选择配置包。
        SelectedPackageNames = ["config"],
        IncludeHotUpdateAssemblies = false
    });
```

项目配置与脚本定义是两种入口：Player 内容构建自动读取项目配置；直接传入
`BuildPackagesAsync` 的包定义决定该次脚本构建的归属。已有 `BuildAsync` 也可在每个
`AssetBundleBuildDefinition.PackageName` 指定归属。
运行时仍用同一 manager 按原 address 加载资源，无需先选择包或为每包创建 manager。
包描述只用于按包检查更新、下载、修复；其 Bundle 依赖闭包会随操作一起验证。
具体运行时 API 见 [runtime-guide.html](Doc/runtime-guide.html)。

内容加密由通用 `BEngine.Content.ContentEncryption` / `ContentEncryptionKey` 提供。
构建设置 `AssetBundleBuildOptions.EncryptionKey`，运行时对应设置
`AssetBundleRuntimeOptions.EncryptionKey`；`RequireEncryptedContent` 可强制拒绝明文包。
加密容器支持分块认证和随机读取，下载/缓存保留密文，运行时解密不写临时明文文件。
PlayerBuild 的主包与热更新流程会自动传递打包时输入的 Key。

AssetBundles 的运行时实现和服务注册由本包持有。依赖见 package.yaml。

Player 内置资源归档支持 v1/v2/v3 读取。PlayerBuild 的通用压缩开关会为主包逐资源选择 Brotli 或原样存储，
按 SHA-256 和原始长度去重；总压缩收益不足以抵消索引开销时回退为 v2。
加载时只打开指定载荷，限制其存储范围及解码长度，再验证原始 SHA-256；压缩与内容加密可以组合使用。
异步读取使用引擎协作调度器，不创建工作线程，不预先分配整个压缩归档。

### AssetBundle 运行时、远程更新与只读契约

`AssetBundleManager.InitializeAsync` 从内置目录、active/previous 指针和本地 content-addressed cache 选择一个已验证状态。每个状态只构建一次不可变 `AssetBundleCatalogIndex`，以大小写不敏感的冻结索引保存 address、bundle、每 bundle 的资产和预计算依赖闭包；`ContainsAddress` 与 `GetDependencyClosure` 可用于检查，`LoadBytesAsync`/`LoadTextAsync` 的热路径不再扫描 catalog。公开构造索引时会克隆 catalog，因此之后修改原对象不会改变索引。

```csharp
using BEngine.AssetBundles;

await using var bundles = new AssetBundleManager(new AssetBundleRuntimeOptions
{
    PackageName = "com.example.game",
    CacheDirectory = cachePath,
    BuiltInDirectory = builtInPath,
    RemoteBaseUri = new Uri("https://cdn.example.test/content/"),
    RequireHttps = true,
    MaxRetries = 3
});

await bundles.InitializeAsync(stop.Token);
AssetBundleUpdatePlan plan = await bundles.CheckForUpdatesAsync(stop.Token);
if (plan.HasUpdates)
    await bundles.ApplyUpdateAsync(plan, progress: null, cancellationToken: stop.Token);

await using AssetBundleHandle<string> text =
    await bundles.LoadTextAsync("Assets/Data/config.json", stop.Token);
```

同一 manager 支持并发初始化和并发加载：相同内容 hash 只创建一个 loaded bundle task，句柄使用引用计数；每个句柄都必须释放，之后可调用 `UnloadUnused`。初始化、Apply、Rollback 和 Cleanup 串行修改管理状态；加载先捕获当前不可变快照，因此更新发布新状态时不会原地改变正在读取的 catalog。并发提交同一更新计划只有一次实际激活。

远程元数据和 bundle 都执行大小、SHA-256 与目录边界校验。bundle 使用 `HttpNetworkRequest` 的流式响应入口逐块写入 staging、增量计算 hash，验证后才移动到 content-addressed object cache；catalog 和 active/previous 指针采用临时文件原子替换。验证失败保留旧 active 状态并清理 `.part`，`RollbackAsync` 只在已验证指针间切换而不重新下载。运行时契约是只读：不要修改 `ActiveCatalog` 后期待影响 manager，不要改写索引或 bundle 内容，也不要让运行时保存回 `Assets`；新版本必须通过构建 catalog、检查、下载、验证和原子 Apply 发布。
