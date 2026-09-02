using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BEngine.Editor;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;

if (args is ["--repack-examples", var authoringRoot])
{
    RepackPublishedExamples(FindRepositoryRoot(), authoringRoot);
    return;
}

if (args is ["--generate-audio-example"])
{
    GenerateAudioExample(FindRepositoryRoot());
    return;
}

RunArchiveContract();
ValidatePublishedExamples(FindRepositoryRoot());
Console.WriteLine(
    "BPACKAGE_ARCHIVES_OK|deterministic,manifest,meta,import,conflicts,overwrite," +
    "published-authored-scenes");

static void RunArchiveContract()
{
    var root = Path.Combine(Path.GetTempPath(), $"BEngine.BPackage.{Guid.NewGuid():N}");
    try
    {
        var source = ProjectWorkspaceFactory.Create(Path.Combine(root, "Source"), "Source");
        var target = ProjectWorkspaceFactory.Create(Path.Combine(root, "Target"), "Target");
        var sampleDirectory = Path.Combine(source.AssetsPath, "Samples");
        Directory.CreateDirectory(sampleDirectory);
        File.WriteAllText(Path.Combine(sampleDirectory, "Greeting.txt"), "hello bpackage");
        new BEngine.ProjectSystem.Editor.AssetDatabase(source).Refresh();

        var first = Path.Combine(root, "First.bpackage");
        var second = Path.Combine(root, "Second.bpackage");
        var options = new BPackageExportOptions
        {
            Name = "Archive Contract",
            Description = "BPackage archive round-trip contract.",
            DefaultImportPath = "Examples/ArchiveContract",
            IncludeMetaFiles = true
        };
        var manifest = BPackageArchive.ExportPackage(source, ["Assets/Samples"], first, options);
        _ = BPackageArchive.ExportPackage(source, ["Assets/Samples"], second, options);

        Require(manifest.Format == "BEngine.BPackage" && manifest.Version == 1, "manifest format");
        Require(manifest.DefaultImportPath == "Examples/ArchiveContract", "default import path");
        Require(manifest.Entries.Any(entry => entry.RelativePath == "Samples/Greeting.txt"), "asset entry");
        Require(manifest.Entries.Any(entry => entry.RelativePath == "Samples/Greeting.txt.meta"), "meta entry");
        Require(SHA256.HashData(File.ReadAllBytes(first)).SequenceEqual(
            SHA256.HashData(File.ReadAllBytes(second))), "deterministic output");

        var read = BPackageArchive.ReadManifest(first);
        Require(read.Entries.Count == manifest.Entries.Count, "manifest inspection");
        var imported = BPackageArchive.ImportPackage(target, first);
        var importedPath = Path.Combine(target.AssetsPath, "Examples", "ArchiveContract", "Samples",
            "Greeting.txt");
        Require(File.ReadAllText(importedPath) == "hello bpackage", "imported content");
        Require(imported.ImportedPaths.Count > 0, "import result");

        var conflicted = false;
        try { _ = BPackageArchive.ImportPackage(target, first); }
        catch (IOException) { conflicted = true; }
        Require(conflicted, "safe conflict default");

        File.WriteAllText(importedPath, "changed");
        _ = BPackageArchive.ImportPackage(target, first,
            new BPackageImportOptions { ConflictPolicy = BPackageConflictPolicy.Overwrite });
        Require(File.ReadAllText(importedPath) == "hello bpackage", "explicit overwrite");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static void ValidatePublishedExamples(string repositoryRoot)
{
    var root = Path.Combine(Path.GetTempPath(), $"BEngine.PublishedExamples.{Guid.NewGuid():N}");
    try
    {
        foreach (var definition in PublishedExamples())
        {
            Console.WriteLine($"BPACKAGE_EXAMPLE_VALIDATE|{definition.Module}");
            var archivePath = Path.Combine(repositoryRoot,
                definition.Archive.Replace('/', Path.DirectorySeparatorChar));
            Require(File.Exists(archivePath), $"published archive {definition.Module}");
            var manifest = BPackageArchive.ReadManifest(archivePath);
            var exampleName = Path.GetFileNameWithoutExtension(definition.Archive);
            var importPath = $"Examples/{exampleName}";
            Require(manifest.DefaultImportPath.Equals(importPath, StringComparison.Ordinal),
                $"default import path {definition.Module}");
            Require(manifest.Entries.All(entry => IsPackageExampleEntry(entry.RelativePath)),
                $"isolated paths {definition.Module}");
            Require(manifest.Entries.Any(entry => entry.RelativePath == "Readme.md"),
                $"README {definition.Module}");
            Require(manifest.Entries.Any(entry => entry.RelativePath == $"res/{definition.Scene}"),
                $"scene {definition.Module}");
            Require(manifest.Entries.Any(entry => entry.RelativePath.EndsWith(".asmdef.yaml",
                StringComparison.OrdinalIgnoreCase)), $"asmdef {definition.Module}");
            Require(manifest.Entries.Where(entry => !entry.IsDirectory &&
                    !entry.RelativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .All(entry => manifest.Entries.Any(meta =>
                    meta.RelativePath.Equals(entry.RelativePath + ".meta", StringComparison.OrdinalIgnoreCase))),
                $"meta coverage {definition.Module}");
            ValidateAuthoredExampleContents(archivePath, definition);

            var workspace = ProjectWorkspaceFactory.Create(
                Path.Combine(root, definition.Module, exampleName), $"{definition.Module} {exampleName}");
            _ = BPackageArchive.ImportPackage(workspace, archivePath,
                new BPackageImportOptions { DestinationDirectory = importPath });
            Require(File.Exists(Path.Combine(workspace.AssetsPath,
                    importPath.Replace('/', Path.DirectorySeparatorChar), "res", definition.Scene)),
                $"individual import {definition.Module}");
        }
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static void ValidateAuthoredExampleContents(
    string archivePath,
    (string Module, string Archive, string Scene, string RequiredComponent) definition)
{
    using var archive = ZipFile.OpenRead(archivePath);
    var scene = ReadArchiveText(archive, $"Assets/res/{definition.Scene}");
    var readme = ReadArchiveText(archive, "Assets/Readme.md");
    var gameObjectCount = Regex.Matches(scene, @"(?m)^- id:\s").Count;
    Require(gameObjectCount >= 2,
        $"authored scene {definition.Module}/{definition.Scene} has only {gameObjectCount} GameObject.");
    Require(scene.Contains("type: BEngine.Camera2D", StringComparison.Ordinal),
        $"authored scene {definition.Module}/{definition.Scene} has no Camera2D.");
    Require(scene.Contains(definition.RequiredComponent, StringComparison.Ordinal),
        $"authored scene {definition.Module}/{definition.Scene} has no {definition.RequiredComponent}.");
    Require(readme.Length >= 800 && readme.Contains("##", StringComparison.Ordinal),
        $"example guide {definition.Module}/{definition.Scene} is not detailed enough.");
    ValidateProjectAssetReferences(archive, archivePath);

    if (Path.GetFileNameWithoutExtension(archivePath).Equals("AtlasPalette", StringComparison.Ordinal))
    {
        var palette = ReadArchiveText(archive, "Assets/res/AtlasPalette.tilepalette.yaml");
        var atlas = ReadArchiveText(archive, "Assets/res/AtlasPalette.atlas.yaml");
        Require(palette.Contains(
                "atlas: Assets/Examples/AtlasPalette/res/AtlasTiles.png", StringComparison.Ordinal),
            "AtlasPalette does not reference its packed texture.");
        Require(archive.GetEntry("Assets/res/AtlasTiles.png") is not null,
            "AtlasPalette packed texture is missing.");
        Require(Regex.Matches(atlas, @"(?m)^- Assets/Examples/AtlasPalette/res/Sources/.+\.png$").Count == 5 &&
                Regex.Matches(atlas, @"(?m)^- name:").Count == 5 &&
                archive.Entries.Where(entry =>
                        entry.FullName.StartsWith("Assets/res/Sources/", StringComparison.Ordinal) &&
                        entry.FullName.EndsWith(".png.meta", StringComparison.OrdinalIgnoreCase))
                    .All(entry => ReadArchiveText(archive, entry.FullName)
                        .Contains("textureType: Sprite", StringComparison.Ordinal)),
            "AtlasPalette TextureAtlas must hold five textures imported as Sprite and packed regions.");
    }
}

static void ValidateProjectAssetReferences(ZipArchive archive, string archivePath)
{
    var exampleName = Path.GetFileNameWithoutExtension(archivePath);
    foreach (var entry in archive.Entries.Where(entry =>
                 entry.FullName.StartsWith("Assets/", StringComparison.Ordinal) &&
                 (entry.FullName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                  entry.FullName.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase))))
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        foreach (Match match in Regex.Matches(text,
                     @"Assets/Examples/(?<example>[A-Za-z0-9._-]+)/(?<path>[A-Za-z0-9_./-]+)"))
        {
            Require(match.Groups["example"].Value.Equals(exampleName, StringComparison.Ordinal),
                $"{exampleName} contains a project asset path for another example: {match.Value}");
            var path = match.Groups["path"].Value.TrimEnd('.');
            if (!Path.HasExtension(path)) continue;
            Require(archive.Entries.Any(candidate => candidate.FullName.TrimEnd('/').Equals(
                        $"Assets/{path}".TrimEnd('/'), StringComparison.OrdinalIgnoreCase)),
                $"{exampleName} references an asset that is absent from the archive: {match.Value}");
        }
    }
}

static string ReadArchiveText(ZipArchive archive, string path)
{
    var entry = archive.GetEntry(path) ??
                throw new InvalidDataException($"Archive entry is missing: {path}");
    using var stream = entry.Open();
    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
    return reader.ReadToEnd();
}

static void RepackPublishedExamples(string repositoryRoot, string authoringRoot)
{
    var sourceRoot = Path.GetFullPath(authoringRoot);
    Require(Directory.Exists(sourceRoot), $"Example authoring root does not exist: {sourceRoot}");
    foreach (var definition in PublishedExamples())
    {
        var exampleName = Path.GetFileNameWithoutExtension(definition.Archive);
        var exampleRoot = Path.Combine(sourceRoot, exampleName);
        var assetsPath = Path.Combine(exampleRoot, "Assets");
        Require(Directory.Exists(assetsPath), $"Example authoring Assets directory is missing: {exampleRoot}");
        var allowedRootEntries = new HashSet<string>(
            ["Editor", "runtime", "res", "Readme.md"], StringComparer.Ordinal);
        var selections = Directory.EnumerateFileSystemEntries(assetsPath)
            .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            .Where(path => allowedRootEntries.Contains(Path.GetFileName(path)))
            .Select(path => $"Assets/{Path.GetFileName(path)}")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Require(selections.Contains("Assets/runtime", StringComparer.Ordinal) &&
                selections.Contains("Assets/res", StringComparer.Ordinal) &&
                selections.Contains("Assets/Readme.md", StringComparer.Ordinal),
            $"Example authoring layout is incomplete: {exampleRoot}");
        var destination = Path.Combine(repositoryRoot,
            definition.Archive.Replace('/', Path.DirectorySeparatorChar));
        var original = BPackageArchive.ReadManifest(destination);
        var projectPath = Path.Combine(exampleRoot, ProjectWorkspace.ProjectFileName);
        if (!File.Exists(projectPath))
            BEngine.YamlUtility.Save(new ProjectData { Name = original.Name }, projectPath);
        var workspace = ProjectWorkspace.Open(exampleRoot);
        new BEngine.ProjectSystem.Editor.AssetDatabase(workspace).Refresh();
        _ = BPackageArchive.ExportPackage(workspace, selections, destination,
            new BPackageExportOptions
            {
                Name = original.Name,
                PackageVersion = original.PackageVersion,
                Description = original.Description,
                DefaultImportPath = original.DefaultImportPath,
                IncludeMetaFiles = true
            });
        Console.WriteLine($"BPACKAGE_EXAMPLE_REPACKED|{definition.Module}|{exampleName}");
    }
}

static void GenerateAudioExample(string repositoryRoot)
{
    var temporaryRoot = Path.Combine(Path.GetTempPath(), $"BEngine.AudioExample.{Guid.NewGuid():N}");
    try
    {
        var workspace = ProjectWorkspaceFactory.Create(temporaryRoot, "Audio Getting Started");
        var editorDirectory = Path.Combine(workspace.AssetsPath, "Editor");
        var runtimeDirectory = Path.Combine(workspace.AssetsPath, "runtime");
        var resourcesDirectory = Path.Combine(workspace.AssetsPath, "res");
        Directory.CreateDirectory(editorDirectory);
        Directory.CreateDirectory(runtimeDirectory);
        Directory.CreateDirectory(resourcesDirectory);
        var generatedProjectMenu = Path.Combine(editorDirectory, "ProjectMenus.cs");
        if (File.Exists(generatedProjectMenu)) File.Delete(generatedProjectMenu);

        File.WriteAllText(Path.Combine(workspace.AssetsPath, "Readme.md"), AudioExampleReadme());
        File.WriteAllText(Path.Combine(runtimeDirectory, "AudioDemo.cs"), AudioExampleScript());
        File.WriteAllText(Path.Combine(runtimeDirectory, "AudioGettingStarted.asmdef.yaml"), AudioExampleAssembly());
        File.WriteAllText(Path.Combine(resourcesDirectory, "Audio.scene.yaml"), AudioExampleScene());
        File.WriteAllBytes(Path.Combine(resourcesDirectory, "AudioTone.wav"), CreateToneWav());

        new BEngine.ProjectSystem.Editor.AssetDatabase(workspace).Refresh();
        var destination = Path.Combine(repositoryRoot, "src", "Packages", "Audio", "Editor", "Examples",
            "AudioGettingStarted.bpackage");
        _ = BPackageArchive.ExportPackage(workspace,
            ["Assets/Editor", "Assets/runtime", "Assets/res", "Assets/Readme.md"], destination,
            new BPackageExportOptions
            {
                Name = "Audio Getting Started",
                PackageVersion = "1.0.0",
                Description = "Complete 2D WAV playback scene with source, listener and runtime controls.",
                DefaultImportPath = "Examples/AudioGettingStarted",
                IncludeMetaFiles = true
            });
        Console.WriteLine($"BPACKAGE_AUDIO_EXAMPLE_GENERATED|{destination}");
        GenerateAudioOneShotExample(repositoryRoot, Path.Combine(temporaryRoot, "OneShotMixer"));
    }
    finally
    {
        if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
    }
}

static void GenerateAudioOneShotExample(string repositoryRoot, string projectRoot)
{
    var workspace = ProjectWorkspaceFactory.Create(projectRoot, "Audio One Shot Mixer");
    var editorDirectory = Path.Combine(workspace.AssetsPath, "Editor");
    var runtimeDirectory = Path.Combine(workspace.AssetsPath, "runtime");
    var resourcesDirectory = Path.Combine(workspace.AssetsPath, "res");
    Directory.CreateDirectory(editorDirectory);
    Directory.CreateDirectory(runtimeDirectory);
    Directory.CreateDirectory(resourcesDirectory);
    var generatedProjectMenu = Path.Combine(editorDirectory, "ProjectMenus.cs");
    if (File.Exists(generatedProjectMenu)) File.Delete(generatedProjectMenu);

    File.WriteAllText(Path.Combine(workspace.AssetsPath, "Readme.md"), AudioOneShotReadme());
    File.WriteAllText(Path.Combine(runtimeDirectory, "OneShotMixerDemo.cs"), AudioOneShotScript());
    File.WriteAllText(Path.Combine(runtimeDirectory, "OneShotMixer.asmdef.yaml"), AudioOneShotAssembly());
    File.WriteAllText(Path.Combine(resourcesDirectory, "OneShotMixer.scene.yaml"), AudioOneShotScene());
    File.WriteAllBytes(Path.Combine(resourcesDirectory, "Click.wav"), CreateToneWav(880, 0.16, 0.35));
    File.WriteAllBytes(Path.Combine(resourcesDirectory, "Confirm.wav"), CreateToneWav(1320, 0.24, 0.25));

    new BEngine.ProjectSystem.Editor.AssetDatabase(workspace).Refresh();
    var destination = Path.Combine(repositoryRoot, "src", "Packages", "Audio", "Editor", "Examples",
        "OneShotMixer.bpackage");
    _ = BPackageArchive.ExportPackage(workspace,
        ["Assets/Editor", "Assets/runtime", "Assets/res", "Assets/Readme.md"], destination,
        new BPackageExportOptions
        {
            Name = "One Shot Mixer",
            PackageVersion = "1.0.0",
            Description = "Complete 2D one-shot sound mixer scene with two clips and stereo controls.",
            DefaultImportPath = "Examples/OneShotMixer",
            IncludeMetaFiles = true
        });
    Console.WriteLine($"BPACKAGE_AUDIO_EXAMPLE_GENERATED|{destination}");
}

static string AudioExampleReadme() =>
    """
    # Audio Getting Started

    这是 BEngine Audio 包的完整 2D 运行时示例，包含一个可循环播放的 PCM WAV、AudioSource、
    AudioListener、Camera2D、可视化 2D 场景和输入控制脚本，不是只有代码的空示例。

    ## 运行

    1. 在 Window > General > Package Manager 启用 Audio 后导入本示例。
    2. 从 Project 打开 `Assets/Examples/AudioGettingStarted/res/Audio.scene.yaml`。
    3. 在 Hierarchy 选择 Music Source；Inspector 中 Audio Clip 应指向 `res/AudioTone.wav`。
    4. 同时打开 Scene、Game 和 Console，点击 Play。示例音源会自动循环播放。
    5. Space 暂停/继续，R 从头播放，Up/Down 调节音量，Left/Right 调节 Stereo Pan。
    6. 点击 Stop；运行时播放游标、音量和声像会恢复为保存的编辑态值。

    ## 场景内容

    - Music Source：SpriteRenderer、AudioSource 和 AudioDemo。Volume 为 0.7，Loop 与 Play On Awake 开启。
    - Audio Listener：场景监听器标记。BEngine Audio 是 2D 模型，不使用 Transform 距离。
    - Audio Stage / Wave Bars：提供可见的 2D 展示内容，确认 Game 窗口渲染正常。
    - Main Camera 2D：正交 2D 相机，显示完整舞台。

    ## Inspector 与资源

    将 Project 中 AudioTone 拖到 Audio Clip ObjectField 可以重新赋值；拖拽时只有类型匹配且鼠标位于
    Field 内才接受。字段选择按钮打开可搜索的 Assets/Scene TreeView。单击字段会 Ping 并展开父文件夹，
    双击才会改变 Selection。Volume、Pitch、Stereo Pan、Priority、Loop 和 Mute 修改均支持 Undo。

    选择 WAV 可查看 AudioClip 的 Samples、Channels、Frequency、Length 和 Load State。Importer 支持
    Force To Mono 与 Normalize。当前示例 WAV 是 44.1 kHz、单声道、16 位 PCM。

    ## 扩展

    在 `AudioDemo.Update` 中加入音效触发可调用 `PlayOneShot`。音乐切换可组合两个 AudioSource 做淡入淡出。
    自定义设备后端实现 `IAudioOutput` 并调用 `AudioOutput.SetBackend`；提交数据为交错 float PCM。
    新音频格式需要同时实现 Editor AssetImporter 与 RuntimeAssetCodecRegistry 解码器，确保 Resources 和
    AssetBundle 在不引用 Editor 程序集的情况下仍可恢复 AudioClip。

    ## 排错

    没有声音时检查 AudioListener.pause、Source Mute/Volume、Clip 引用和 OpenAL。OpenAL 不可用时引擎会
    退化为 Null 输出，Console 会给出原因，但场景逻辑仍正常。该包没有 3D spatial blend、距离衰减或 doppler。
    """;

static string AudioExampleScript() =>
    """
    using BEngine;
    using BEngine.Audio;

    namespace BEngine.Examples.Audio;

    public sealed class AudioDemo : MonoBehaviour
    {
        private AudioSource? _source;

        public override void Start()
        {
            _source = GetComponent<AudioSource>();
            Debug.Log("Audio example ready: Space pause, R restart, arrows change volume and pan.");
        }

        public override void Update()
        {
            if (_source is null) return;
            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (_source.isPlaying) _source.Pause();
                else _source.UnPause();
            }
            if (Input.GetKeyDown(KeyCode.R)) _source.Play();
            if (Input.GetKey(KeyCode.UpArrow))
                _source.volume = Fix64.Min(Fix64.One, _source.volume + Time.deltaTime);
            if (Input.GetKey(KeyCode.DownArrow))
                _source.volume = Fix64.Max(Fix64.Zero, _source.volume - Time.deltaTime);
            if (Input.GetKey(KeyCode.LeftArrow))
                _source.panStereo = Fix64.Max(-Fix64.One, _source.panStereo - Time.deltaTime);
            if (Input.GetKey(KeyCode.RightArrow))
                _source.panStereo = Fix64.Min(Fix64.One, _source.panStereo + Time.deltaTime);
        }
    }
    """;

static string AudioExampleAssembly() =>
    """
    format: BEngine.AssemblyDefinition
    version: 1
    name: BEngine.Examples.Audio.GettingStarted
    rootNamespace: BEngine.Examples.Audio
    references:
    - BEngine.Audio
    includePlatforms: []
    excludePlatforms: []
    defineConstraints: []
    autoReferenced: true
    editorOnly: false
    allowUnsafeCode: false
    """;

static string AudioOneShotReadme() =>
    """
    # One Shot Mixer

    这是 BEngine Audio 的第二个完整运行时场景，演示一个 AudioSource 同时播放多个短音效、独立音量缩放、
    2D 左右声像和全局 Listener 控制。示例包含两个真实 PCM WAV、场景、运行脚本、asmdef 与操作说明。

    ## 运行

    1. 在 Package Manager 启用 Audio，导入 One Shot Mixer。
    2. 从 Project 打开 `Assets/Examples/OneShotMixer/res/OneShotMixer.scene.yaml`。
    3. 选择 One Shot Source，在 Inspector 检查两个脚本 ObjectField 和 AudioSource 参数。
    4. 打开 Game 与 Console 后 Play。按 Space 播放 Click，Enter 播放 Confirm，左右方向键移动 Stereo Pan。
    5. 按 M 切换 Source Mute，按 P 切换 `AudioListener.pause`；连续快速按键可验证多 voice 混音。
    6. Pause/Step 可逐帧观察；Stop 后运行时 voice、mute、pan 与全局 pause 不保存。

    ## 场景

    One Shot Source 包含 SpriteRenderer、AudioSource 和 OneShotMixerDemo。AudioSource 不自动播放固定 Clip，
    脚本持有 Click.wav 与 Confirm.wav 两个 AudioClip 引用，并调用 `PlayOneShot`。Audio Listener 提供全局
    volume/pause。Main Camera 2D 和可见的 Left/Right 声道条让示例在 Game 窗口有完整展示。

    ## API 与扩展

    `PlayOneShot(clip)` 使用完整增益；`PlayOneShot(clip, scale)` 将 scale 限制在 0 到 1，显式传 0 会静音。
    每个 one-shot 维护自己的样本游标，完成后从 voice 列表移除。可在此基础上实现音效池、UI 声音路由、
    随机 pitch 或音乐淡入淡出。BEngine 是 2D 引擎，Pan 只作用于左右声道，不读取 Transform 距离。

    ## 排错

    没有声音时检查 Listener.pause、Source mute/volume、两个 Clip ObjectField 和 OpenAL 后端。拖拽赋值时
    只有 AudioClip 类型可接受；若父文件夹折叠，Ping 会自动展开 Project 完整父链。Console 会展示 WAV
    解码和设备初始化错误，Profiler Runtime 域可查看 Audio Mix 调用与分配。
    """;

static string AudioOneShotScript() =>
    """
    using BEngine;
    using BEngine.Audio;

    namespace BEngine.Examples.Audio;

    public sealed class OneShotMixerDemo : MonoBehaviour
    {
        public AudioClip? click;
        public AudioClip? confirm;
        private AudioSource? _source;

        public override void Start()
        {
            _source = GetComponent<AudioSource>();
            AudioListener.pause = false;
            Debug.Log("One Shot Mixer: Space click, Enter confirm, arrows pan, M mute, P pause.");
        }

        public override void Update()
        {
            if (_source is null) return;
            if (Input.GetKeyDown(KeyCode.Space) && click is not null)
                _source.PlayOneShot(click, Fix64.Parse("0.7"));
            if (Input.GetKeyDown(KeyCode.Return) && confirm is not null)
                _source.PlayOneShot(confirm);
            if (Input.GetKeyDown(KeyCode.M)) _source.mute = !_source.mute;
            if (Input.GetKeyDown(KeyCode.P)) AudioListener.pause = !AudioListener.pause;
            if (Input.GetKey(KeyCode.LeftArrow))
                _source.panStereo = Fix64.Max(-Fix64.One, _source.panStereo - Time.deltaTime * 2);
            if (Input.GetKey(KeyCode.RightArrow))
                _source.panStereo = Fix64.Min(Fix64.One, _source.panStereo + Time.deltaTime * 2);
        }

        public override void OnDisable() => AudioListener.pause = false;
    }
    """;

static string AudioOneShotAssembly() =>
    """
    format: BEngine.AssemblyDefinition
    version: 1
    name: BEngine.Examples.Audio.OneShotMixer
    rootNamespace: BEngine.Examples.Audio
    references:
    - BEngine.Audio
    includePlatforms: []
    excludePlatforms: []
    defineConstraints: []
    autoReferenced: true
    editorOnly: false
    allowUnsafeCode: false
    """;

static string AudioOneShotScene() =>
    """
    format: BEngine.Scene
    version: 1
    id: 7d9d0365-4a15-4a08-9a98-5b39bcb20001
    name: One Shot Mixer
    gameObjects:
    - id: 7d9d0365-4a15-4a08-9a98-5b39bcb20002
      name: Mixer Stage
      active: true
      tag: Untagged
      layer: 2
      isStatic: true
      transform:
        id: 7d9d0365-4a15-4a08-9a98-5b39bcb20003
        type: BEngine.Transform
        localPosition: { x: 0, y: 0 }
        localRotation: 0
        localScale: { x: 1, y: 1 }
        fields: {}
      components:
      - id: 7d9d0365-4a15-4a08-9a98-5b39bcb20004
        type: BEngine.SpriteRenderer
        enabled: true
        fields:
          color: 0.055,0.08,0.11,1
          opacity: 1
          orderInLayer: -20
          size: 12,8
          sortingLayer: 2
    - id: 7d9d0365-4a15-4a08-9a98-5b39bcb20005
      name: One Shot Source
      active: true
      tag: Untagged
      layer: 2
      isStatic: false
      transform:
        id: 7d9d0365-4a15-4a08-9a98-5b39bcb20006
        type: BEngine.Transform
        localPosition: { x: 0, y: 0.4 }
        localRotation: 0
        localScale: { x: 1, y: 1 }
        fields: {}
      components:
      - id: 7d9d0365-4a15-4a08-9a98-5b39bcb20007
        type: BEngine.SpriteRenderer
        enabled: true
        fields:
          color: 0.18,0.58,0.84,1
          opacity: 1
          orderInLayer: 8
          size: 2.2,2.2
          sortingLayer: 2
      - id: 7d9d0365-4a15-4a08-9a98-5b39bcb20008
        type: BEngine.Audio.AudioSource
        enabled: true
        fields:
          loop: false
          mute: false
          panStereo: 0
          pitch: 1
          playOnAwake: false
          priority: 96
          volume: 0.85
      - id: 7d9d0365-4a15-4a08-9a98-5b39bcb20009
        type: BEngine.Examples.Audio.OneShotMixerDemo
        enabled: true
        fields:
          click: Assets/Examples/OneShotMixer/res/Click.wav
          confirm: Assets/Examples/OneShotMixer/res/Confirm.wav
    - id: 7d9d0365-4a15-4a08-9a98-5b39bcb20010
      name: Audio Listener
      active: true
      tag: Untagged
      layer: 2
      isStatic: false
      transform:
        id: 7d9d0365-4a15-4a08-9a98-5b39bcb20011
        type: BEngine.Transform
        localPosition: { x: 0, y: -2.2 }
        localRotation: 0
        localScale: { x: 1, y: 1 }
        fields: {}
      components:
      - id: 7d9d0365-4a15-4a08-9a98-5b39bcb20012
        type: BEngine.Audio.AudioListener
        enabled: true
        fields: {}
    - id: 7d9d0365-4a15-4a08-9a98-5b39bcb20013
      name: Left Channel
      active: true
      tag: Untagged
      layer: 2
      isStatic: true
      transform:
        id: 7d9d0365-4a15-4a08-9a98-5b39bcb20014
        type: BEngine.Transform
        localPosition: { x: -3.2, y: -1.8 }
        localRotation: 0
        localScale: { x: 1, y: 1 }
        fields: {}
      components:
      - id: 7d9d0365-4a15-4a08-9a98-5b39bcb20015
        type: BEngine.SpriteRenderer
        enabled: true
        fields:
          color: 0.17,0.74,0.66,1
          opacity: 1
          orderInLayer: 3
          size: 3.2,0.55
          sortingLayer: 2
    - id: 7d9d0365-4a15-4a08-9a98-5b39bcb20016
      name: Right Channel
      active: true
      tag: Untagged
      layer: 2
      isStatic: true
      transform:
        id: 7d9d0365-4a15-4a08-9a98-5b39bcb20017
        type: BEngine.Transform
        localPosition: { x: 3.2, y: -1.8 }
        localRotation: 0
        localScale: { x: 1, y: 1 }
        fields: {}
      components:
      - id: 7d9d0365-4a15-4a08-9a98-5b39bcb20018
        type: BEngine.SpriteRenderer
        enabled: true
        fields:
          color: 0.89,0.66,0.26,1
          opacity: 1
          orderInLayer: 3
          size: 3.2,0.55
          sortingLayer: 2
    - id: 7d9d0365-4a15-4a08-9a98-5b39bcb20019
      name: Main Camera 2D
      active: true
      tag: MainCamera
      layer: 2
      isStatic: false
      transform:
        id: 7d9d0365-4a15-4a08-9a98-5b39bcb20020
        type: BEngine.Transform
        localPosition: { x: 0, y: 0 }
        localRotation: 0
        localScale: { x: 1, y: 1 }
        fields: {}
      components:
      - id: 7d9d0365-4a15-4a08-9a98-5b39bcb20021
        type: BEngine.Camera2D
        enabled: true
        fields:
          backgroundColor: 0.035,0.05,0.07,1
          cullingMask: -1
          isMain: true
          size: 5
    """;

static string AudioExampleScene() =>
    """
    format: BEngine.Scene
    version: 1
    id: 6c8c9254-3904-49f7-8987-4a28aba10001
    name: Audio Getting Started
    gameObjects:
    - id: 6c8c9254-3904-49f7-8987-4a28aba10002
      name: Audio Stage
      active: true
      tag: Untagged
      layer: 2
      isStatic: true
      transform:
        id: 6c8c9254-3904-49f7-8987-4a28aba10003
        type: BEngine.Transform
        localPosition: { x: 0, y: 0 }
        localRotation: 0
        localScale: { x: 1, y: 1 }
        fields: {}
      components:
      - id: 6c8c9254-3904-49f7-8987-4a28aba10004
        type: BEngine.SpriteRenderer
        enabled: true
        fields:
          color: 0.08,0.12,0.16,1
          opacity: 1
          orderInLayer: -20
          size: 12,8
          sortingLayer: 2
    - id: 6c8c9254-3904-49f7-8987-4a28aba10005
      name: Music Source
      active: true
      tag: Untagged
      layer: 2
      isStatic: false
      transform:
        id: 6c8c9254-3904-49f7-8987-4a28aba10006
        type: BEngine.Transform
        localPosition: { x: -1.5, y: 0.2 }
        localRotation: 0
        localScale: { x: 1, y: 1 }
        fields: {}
      components:
      - id: 6c8c9254-3904-49f7-8987-4a28aba10007
        type: BEngine.SpriteRenderer
        enabled: true
        fields:
          color: 0.17,0.74,0.66,1
          opacity: 1
          orderInLayer: 8
          size: 2,2
          sortingLayer: 2
      - id: 6c8c9254-3904-49f7-8987-4a28aba10008
        type: BEngine.Audio.AudioSource
        enabled: true
        fields:
          clip: Assets/Examples/AudioGettingStarted/res/AudioTone.wav
          loop: true
          mute: false
          panStereo: 0
          pitch: 1
          playOnAwake: true
          priority: 128
          volume: 0.7
      - id: 6c8c9254-3904-49f7-8987-4a28aba10009
        type: BEngine.Examples.Audio.AudioDemo
        enabled: true
        fields: {}
    - id: 6c8c9254-3904-49f7-8987-4a28aba10010
      name: Audio Listener
      active: true
      tag: Untagged
      layer: 2
      isStatic: false
      transform:
        id: 6c8c9254-3904-49f7-8987-4a28aba10011
        type: BEngine.Transform
        localPosition: { x: 2, y: 0.2 }
        localRotation: 0
        localScale: { x: 1, y: 1 }
        fields: {}
      components:
      - id: 6c8c9254-3904-49f7-8987-4a28aba10012
        type: BEngine.SpriteRenderer
        enabled: true
        fields:
          color: 0.89,0.66,0.26,1
          opacity: 1
          orderInLayer: 7
          size: 1.3,1.3
          sortingLayer: 2
      - id: 6c8c9254-3904-49f7-8987-4a28aba10013
        type: BEngine.Audio.AudioListener
        enabled: true
        fields: {}
    - id: 6c8c9254-3904-49f7-8987-4a28aba10014
      name: Main Camera 2D
      active: true
      tag: MainCamera
      layer: 2
      isStatic: false
      transform:
        id: 6c8c9254-3904-49f7-8987-4a28aba10015
        type: BEngine.Transform
        localPosition: { x: 0, y: 0 }
        localRotation: 0
        localScale: { x: 1, y: 1 }
        fields: {}
      components:
      - id: 6c8c9254-3904-49f7-8987-4a28aba10016
        type: BEngine.Camera2D
        enabled: true
        fields:
          backgroundColor: 0.04,0.055,0.075,1
          cullingMask: -1
          isMain: true
          size: 5
    """;

static byte[] CreateToneWav(double frequency = 440, double durationSeconds = 1, double amplitude = 0.22)
{
    const int sampleRate = 44100;
    var sampleCount = Math.Max(1, (int)Math.Round(sampleRate * durationSeconds));
    const short channels = 1;
    const short bitsPerSample = 16;
    using var stream = new MemoryStream(44 + sampleCount * sizeof(short));
    using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
    writer.Write(Encoding.ASCII.GetBytes("RIFF"));
    writer.Write(36 + sampleCount * sizeof(short));
    writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
    writer.Write(16);
    writer.Write((short)1);
    writer.Write(channels);
    writer.Write(sampleRate);
    writer.Write(sampleRate * channels * bitsPerSample / 8);
    writer.Write((short)(channels * bitsPerSample / 8));
    writer.Write(bitsPerSample);
    writer.Write(Encoding.ASCII.GetBytes("data"));
    writer.Write(sampleCount * sizeof(short));
    for (var index = 0; index < sampleCount; index++)
    {
        var envelope = Math.Min(1d, index / 800d) * Math.Min(1d, (sampleCount - index) / 1200d);
        var sample = Math.Sin(2d * Math.PI * frequency * index / sampleRate) * amplitude * envelope;
        writer.Write((short)Math.Round(sample * short.MaxValue));
    }
    writer.Flush();
    return stream.ToArray();
}

static (string Module, string Archive, string Scene, string RequiredComponent)[] PublishedExamples() =>
[
    ("Core", "src/Core/Editor/Examples/CoreGettingStarted.bpackage", "Core.scene.yaml",
        "type: BEngine.SpriteRenderer"),
    ("Animation", "src/Packages/Animation/Editor/Examples/AnimationGettingStarted.bpackage",
        "Animation.scene.yaml", "type: BEngine.Animation.Animator"),
    ("Animation", "src/Packages/Animation/Editor/Examples/StateMachine.bpackage",
        "StateMachine.scene.yaml", "type: BEngine.Animation.Animator"),
    ("Audio", "src/Packages/Audio/Editor/Examples/AudioGettingStarted.bpackage",
        "Audio.scene.yaml", "type: BEngine.Audio.AudioSource"),
    ("Audio", "src/Packages/Audio/Editor/Examples/OneShotMixer.bpackage",
        "OneShotMixer.scene.yaml", "type: BEngine.Audio.AudioSource"),
    ("Navigation2D", "src/Packages/Navigation2D/Editor/Examples/NavigationSurfaceAndAgent.bpackage",
        "Navigation.scene.yaml", "type: BEngine.Navigation2D.NavigationSurface2D"),
    ("Navigation2D", "src/Packages/Navigation2D/Editor/Examples/DynamicRebake.bpackage",
        "DynamicRebake.scene.yaml", "type: BEngine.Navigation2D.NavigationSurface2D"),
    ("Physics2D", "src/Packages/Physics2D/Editor/Examples/RigidbodyAndQueries.bpackage",
        "Physics2D.scene.yaml", "type: BEngine.Physics2D.Rigidbody2D"),
    ("Physics2D", "src/Packages/Physics2D/Editor/Examples/TriggersAndQueries.bpackage",
        "TriggersAndQueries.scene.yaml", "type: BEngine.Physics2D.BoxCollider2D"),
    ("PropertyAttributes",
        "src/Packages/PropertyAttributes/Editor/Examples/InspectorAttributesAndDrawer.bpackage",
        "PropertyAttributes.scene.yaml", "type: BEngine.Examples.PropertyAttributes"),
    ("PropertyAttributes", "src/Packages/PropertyAttributes/Editor/Examples/AttributesGallery.bpackage",
        "AttributesGallery.scene.yaml", "type: BEngine.Examples.PropertyAttributes"),
    ("TiledMap", "src/Packages/TiledMap/Editor/Examples/RuntimePainting.bpackage",
        "RuntimePainting.scene.yaml", "type: BEngine.TiledMap.Tilemap"),
    ("TiledMap", "src/Packages/TiledMap/Editor/Examples/AtlasPalette.bpackage",
        "AtlasPalette.scene.yaml", "type: BEngine.TiledMap.Tilemap"),
    ("UIElements", "src/Packages/UIElements/Editor/Examples/RuntimeHud.bpackage",
        "UIElements.scene.yaml", "type: BEngine.UIElements.UIDocument"),
    ("UIElements", "src/Packages/UIElements/Editor/Examples/ControlsGallery.bpackage",
        "ControlsGallery.scene.yaml", "type: BEngine.UIElements.UIDocument")
];

static bool IsPackageExampleEntry(string relativePath)
{
    var path = relativePath.Replace('\\', '/').Trim('/');
    var topLevel = path.Split('/', 2)[0];
    if (topLevel is "Editor" or "runtime" or "res") return true;
    return path.Equals("Editor.meta", StringComparison.Ordinal) ||
           path.Equals("runtime.meta", StringComparison.Ordinal) ||
           path.Equals("res.meta", StringComparison.Ordinal) ||
           path.Equals("Readme.md", StringComparison.OrdinalIgnoreCase) ||
           path.Equals("Readme.md.meta", StringComparison.OrdinalIgnoreCase);
}

static string FindRepositoryRoot()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
         directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln")))
            return directory.FullName;
    throw new DirectoryNotFoundException("Could not locate the BEngine repository root.");
}

static void Require(bool condition, string scenario)
{
    if (!condition) throw new InvalidOperationException($"BPackage contract failed: {scenario}.");
}
