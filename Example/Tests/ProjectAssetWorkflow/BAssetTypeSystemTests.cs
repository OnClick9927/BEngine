using System.Collections;
using System.Linq.Expressions;
using BEngine.Animation;
using BEngine.Editor;
using BEngine.ProjectSystem;
using BEngine.Rendering;
using BEngine.Serialization;
using System.Reflection;
using AotProjectLayout = BEngine.ProjectSystem.Editor.AotProjectLayout;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;
using ProjectAssetChange = BEngine.ProjectSystem.Editor.AssetChange;
using InspectorEditor = BEngine.Editor.Editor;

namespace BEngine.ExampleTests.ProjectAssetWorkflow;

internal static class BAssetTypeSystemTests
{
    private static object? _capturedObjectPickerItems;

    internal static void Run()
    {
        if (Execute() != 0)
            throw new InvalidOperationException("BAsset type-system regression checks failed.");
    }

    private static int Execute()
    {
        var root = Path.Combine(Path.GetTempPath(), "BEngineBAssetTypeSystem", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        TestEditorHost? host = null;
        try
        {
            new ProjectData { Name = "BAsset Type Test" }.Save(Path.Combine(root, "Project.yaml"));
            var workspace = ProjectWorkspace.Open(root);
            var animationEditor = Assembly.LoadFrom(
                Path.Combine(AppContext.BaseDirectory, "BEngine.Animation.Editor.dll"));
            TypeCache.Refresh();
            EditorInitialization.Run([animationEditor], scriptsReloaded: false);
            AssetTypeRegistry.Register<ExternalYamlAsset>(".external.yaml", "External YAML",
                EditorBuiltinIcons.Assets.Data);
            WriteFixtures(workspace);
            var projectAssets = new ProjectAssetDatabase(workspace);
            projectAssets.Refresh();
            host = new TestEditorHost(workspace, projectAssets);
            EditorBridge.Attach(host);

            VerifyRequiredRuntimeTypes();
            VerifyTypedLoads();
            VerifyUnsupportedTextureFormats(workspace, projectAssets);
            VerifyDynamicTypeRegistration();
            VerifyTextureImporterRoundTrip(workspace);
            VerifyManagedAssetPersistence();
            VerifySubAssetPersistence(workspace, projectAssets);
            VerifyAotAssetProtection(workspace, host);
            VerifyTextureAtlasGuidReferencesAndGeneratedSubAsset(workspace, projectAssets, host);
            VerifyBAssetReferenceRoundTrip();
            VerifyPackageReference(workspace);
            VerifyInheritedAndUnloadableIcons(root);

            Console.WriteLine("BASSET_TYPE_SYSTEM_OK|typed-files,png-only-texture-import," +
                              "texture-meta-roundtrip,importer-revert," +
                              "managed-save-reload,subasset-guid-localid,file-subasset-reference," +
                              "atlas-guid-sources,atlas-library-artifact,atlas-importer,atlas-png-subasset," +
                              "atlas-incremental-subasset,atlas-stable-import,atlas-empty-sources-cleanup," +
                               "subasset-cache-invalidation,subasset-identity-collision,atlas-stable-move," +
                               "subasset-delete-guard,aot-asset-protection," +
                              "basset-reference-roundtrip,package-reference," +
                              "no-loader-registry,editor-icon-inheritance,registry-unload");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"BASSET_TYPE_SYSTEM_FAILED|{exception}");
            return 1;
        }
        finally
        {
            if (host is not null)
            {
                EditorBridge.Detach(host);
                host.Dispose();
            }
            Resources.UnregisterResourceRoot(root);
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void VerifyAotAssetProtection(ProjectWorkspace workspace, TestEditorHost host)
    {
        var aotRoot = workspace.ResolveInside(AotProjectLayout.AssetRoot);
        var scenePath = workspace.ResolveInside(AotProjectLayout.SceneAssetPath);
        Directory.CreateDirectory(aotRoot);
        File.WriteAllText(scenePath, "format: BEngine.Scene\nversion: 2\nname: AOT\ngameObjects: []\n");

        var hostDeleteReached = false;
        host.DeleteAssetFailure = _ =>
        {
            hostDeleteReached = true;
            return false;
        };
        var sceneDeleteRejected = !AssetDatabase.DeleteAsset("assets\\AOT\\AOT.scene.yaml");
        var rootDeleteRejected = !AssetDatabase.DeleteAsset("Assets/Aot");
        host.DeleteAssetFailure = null;

        var hostMoveReached = false;
        host.MoveAssetFailure = (_, _) =>
        {
            hostMoveReached = true;
            return null;
        };
        var sceneMoveRejected = AssetDatabase.MoveAsset(
            AotProjectLayout.SceneAssetPath, "Assets/Scenes/Renamed.scene.yaml");
        var rootMoveRejected = AssetDatabase.MoveAsset(AotProjectLayout.AssetRoot, "Assets/Bootstrap");
        host.MoveAssetFailure = null;

        Require(sceneDeleteRejected && rootDeleteRejected && !hostDeleteReached &&
                sceneMoveRejected.Contains("AOT", StringComparison.Ordinal) &&
                rootMoveRejected.Contains("AOT", StringComparison.Ordinal) && !hostMoveReached &&
                File.Exists(scenePath) && Directory.Exists(aotRoot),
            "The public AssetDatabase allowed deleting or moving the reserved AOT folder/scene identity.");
    }

    private static void VerifyRequiredRuntimeTypes()
    {
        Type[] types =
        [
            typeof(BEngine.Font), typeof(BEngine.Texture), typeof(BEngine.Script),
            typeof(Shader), typeof(Material), typeof(PrefabAsset), typeof(Scene), typeof(BEngine.TextAsset),
            typeof(ScriptableObject), typeof(TextureAtlas), typeof(AnimationClip), typeof(AnimatorController)
        ];
        Require(types.All(type => typeof(BAsset).IsAssignableFrom(type)) &&
                typeof(BObject).IsAssignableFrom(typeof(Sprite)) &&
                !typeof(BAsset).IsAssignableFrom(typeof(Sprite)),
            "The unified BAsset hierarchy or Texture-created Sprite object classification is incorrect.");
    }

    private static void VerifyTypedLoads()
    {
        var texture = AssetDatabase.LoadAssetAtPath<BEngine.Texture>("Assets/Spark.png");
        Require(texture is { width: 1, height: 1 } && texture.assetPath == "Assets/Spark.png",
            "Texture did not load as a bound BEngine.Texture.");
        Require(AssetPreview.GetAssetPreview(texture!) is { Width: 1, Height: 1 },
            "Typed Texture did not use the raster AssetPreview path.");
        Require(AssetDatabase.LoadAssetAtPath<BEngine.Font>("Assets/Test.ttf") is not null,
            "Font did not load as BEngine.Font.");
        Require(AssetDatabase.LoadAssetAtPath<BEngine.Script>("Assets/Probe.cs") is { text.Length: > 0 },
            "Script did not load as BEngine.Script.");
        Require(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Test.shader") is { sourceCode.Length: > 0 },
            "Shader did not load as a source-backed Shader BAsset.");
        Require(AssetDatabase.LoadAssetAtPath<Scene>("Assets/Test.scene.yaml") is { name: "Asset Scene" },
            "Scene did not load as a Scene BAsset.");
        Require(AssetDatabase.LoadAssetAtPath<Material>("Assets/Test.material.yaml") is
                { renderQueue: 2450 } material && material.color.Equals(new Color(1, 0, 0, 1)),
            "Material did not round-trip through its typed loader.");
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Spark.png");
        Require(sprite is { Texture: "Assets/Spark.png", LocalIdentifier: 21300000 },
            "A Sprite-mode Texture did not expose its imported Sprite representation.");
        Require(AssetDatabase.LoadAssetAtPath<TextureAtlas>("Assets/Test.atlas.yaml") is not null,
            "TextureAtlas did not use the registered typed loader.");
        Require(AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Test.anim.yaml") is not null &&
                AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Test.controller.yaml") is not null,
            "Animation BAsset types did not use their dynamically loaded package registrations.");
        Require(AssetDatabase.LoadAssetAtPath<ExternalYamlAsset>("Assets/Test.external.yaml") is
                { strength: 9, assetPath: "Assets/Test.external.yaml" },
            "A dynamically registered BAsset without an explicit loader did not use the generic loader.");
        using var textureEditor = InspectorEditor.CreateEditor(texture!);
        Require(textureEditor.GetType() == typeof(BAssetEditor),
            "Texture did not resolve the importer-backed BAssetEditor.");

        Require(!typeof(BAsset).IsAssignableFrom(sprite!.GetType()),
            "An imported Sprite representation was incorrectly exposed as a standalone BAsset.");
    }

    private static void VerifyDynamicTypeRegistration()
    {
        Require(AssetTypeRegistry.ResolveAssetType("Assets/Test.external.yaml") == typeof(ExternalYamlAsset),
            "The dynamic asset type registration was not discoverable.");
        AssetTypeRegistry.UnregisterAssembly(typeof(ExternalYamlAsset).Assembly);
        Require(AssetTypeRegistry.ResolveAssetType("Assets/Test.external.yaml") is null,
            "Assembly unload did not clear a dynamic asset type registration.");
    }

    private static void VerifyUnsupportedTextureFormats(
        ProjectWorkspace workspace,
        ProjectAssetDatabase projectAssets)
    {
        var unsupported = projectAssets.GetRecord("Assets/Unsupported.jpg");
        var meta = BEngine.YamlUtility.Load<BEngine.ProjectSystem.Editor.AssetMetaDocument>(
            Path.Combine(workspace.AssetsPath, "Unsupported.jpg.meta"));
        Require(unsupported is { AssetType: "DefaultAsset" } &&
                meta.Importer == nameof(DefaultImporter) &&
                AssetImporter.GetAtPath("Assets/Unsupported.jpg") is DefaultImporter &&
                AssetDatabase.LoadAssetAtPath<BEngine.Texture>("Assets/Unsupported.jpg") is null &&
                AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Unsupported.jpg") is null,
            "An unsupported JPG was imported or loaded as a Texture/Sprite.");
        Require(new[] { ".jpg", ".jpeg", ".bmp", ".tga", ".webp" }.All(extension =>
                AssetTypeRegistry.ResolveAssetType("Assets/Unsupported" + extension) is null &&
                AssetTypeRegistry.ResolveImporterType("Assets/Unsupported" + extension) is null),
            "An unsupported image suffix is still registered as a TextureImporter source.");
    }

    private static void VerifyTextureImporterRoundTrip(ProjectWorkspace workspace)
    {
        var importer = AssetImporter.GetAtPath("Assets/Spark.png") as TextureImporter ??
                       throw new InvalidOperationException("TextureImporter was not selected for PNG.");
        importer.textureType = TextureImporterType.Sprite;
        importer.sRGBTexture = false;
        importer.alphaIsTransparency = true;
        importer.isReadable = true;
        importer.compressionFormat = TextureCompressionFormat.Bc7;
        importer.filterMode = TextureFilterMode.Point;
        importer.wrapMode = TextureWrapMode.Mirror;
        importer.generateMipMaps = true;
        importer.maxTextureSize = 4096;
        importer.pixelsPerUnit = 64;
        importer.spritePivotX = 0.25f;
        importer.spritePivotY = 0.75f;
        importer.userData = "round-trip";
        importer.SaveAndReimport();

        var reloaded = AssetImporter.GetAtPath("Assets/Spark.png") as TextureImporter;
        Require(reloaded is
        {
            textureType: TextureImporterType.Sprite,
            sRGBTexture: false,
            alphaIsTransparency: true,
            isReadable: true,
            compressionFormat: TextureCompressionFormat.Bc7,
            filterMode: TextureFilterMode.Point,
            wrapMode: TextureWrapMode.Mirror,
            generateMipMaps: true,
            maxTextureSize: 4096,
            pixelsPerUnit: 64,
            spritePivotX: 0.25f,
            spritePivotY: 0.75f,
            userData: "round-trip"
        }, "Texture import settings did not reload from .meta Settings.");
        reloaded!.compressionFormat = TextureCompressionFormat.Rgba32;
        reloaded.Revert();
        Require(reloaded.compressionFormat == TextureCompressionFormat.Bc7,
            "TextureImporter.Revert did not restore persisted settings.");

        var texture = AssetDatabase.LoadAssetAtPath<BEngine.Texture>("Assets/Spark.png");
        Require(texture is
            {
                compressionFormat: TextureCompressionFormat.Bc7,
                mipMaps: true,
                pixelsPerUnit: 64,
                sRGB: false,
                alphaIsTransparency: true,
                isReadable: true,
                filterMode: TextureFilterMode.Point,
                wrapMode: TextureWrapMode.Mirror
            },
            "Reloaded Texture did not consume persisted importer settings.");
        var importedSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Spark.png");
        Require(importedSprite is
                {
                    Texture: "Assets/Spark.png",
                    PivotX: 0.25f,
                    PivotY: 0.75f,
                    assetPath: "Assets/Spark.png"
                } && importedSprite.Id == texture!.Id,
            "A Sprite-mode texture did not expose its imported Sprite sub-asset view.");
        var allAssets = AssetDatabase.LoadAllAssetsAtPath("Assets/Spark.png");
        Require(allAssets is [BEngine.Texture, Sprite] &&
                AssetDatabase.GetMainAssetTypeAtPath("Assets/Spark.png") == typeof(BEngine.Texture),
            "Texture main-asset identity or imported Sprite sub-asset enumeration is incorrect.");
        var meta = BEngine.YamlUtility.Load<BEngine.ProjectSystem.Editor.AssetMetaDocument>(
            Path.Combine(workspace.AssetsPath, "Spark.png.meta"));
        Require(meta.Settings["compressionFormat"] == "Bc7" &&
                meta.Settings["textureType"] == "Sprite" &&
                meta.Settings["spritePivotX"] == "0.25" &&
                meta.Settings["spritePivotY"] == "0.75" &&
                meta.Importer == nameof(TextureImporter),
            "Texture requested compression format was not written to metadata.");

        VerifyImportedSpriteWriteProtection(workspace, importedSprite!);
        VerifySpriteObjectPicker(texture!);
    }

    private static void VerifyImportedSpriteWriteProtection(ProjectWorkspace workspace, Sprite importedSprite)
    {
        var sourcePath = Path.Combine(workspace.AssetsPath, "Spark.png");
        var originalBytes = File.ReadAllBytes(sourcePath);
        var originalHash = System.Security.Cryptography.SHA256.HashData(originalBytes);

        importedSprite.PivotX = 0.9f;
        EditorUtility.SetDirty(importedSprite);
        Require(File.ReadAllBytes(sourcePath).SequenceEqual(originalBytes) &&
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(sourcePath)).SequenceEqual(originalHash),
            "Editing a transient Sprite representation changed the source PNG bytes.");

        using var editor = InspectorEditor.CreateEditor(importedSprite);
        Require(editor is SpriteEditor, "An imported Sprite did not resolve the Sprite Inspector.");
        editor.SaveChanges();
        Require(File.ReadAllBytes(sourcePath).SequenceEqual(originalBytes),
            "SpriteEditor SaveChanges changed the source PNG bytes for an imported Sprite.");
    }

    private static void VerifySpriteObjectPicker(BEngine.Texture spriteTexture)
    {
        var plainTexture = AssetDatabase.LoadAssetAtPath<BEngine.Texture>("Assets/Plain.png") ??
                           throw new InvalidOperationException("Default Texture fixture did not load.");
        Require(AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Plain.png") is null,
            "A Texture-mode image unexpectedly exposed a Sprite sub-asset.");

        var editorAssembly = typeof(EditorWindow).Assembly;
        var picker = editorAssembly.GetType("BEngine.Editor.EditorObjectPicker", throwOnError: true)!;
        var open = picker.GetMethod("Open", BindingFlags.Static | BindingFlags.NonPublic)!;
        var tryConsume = picker.GetMethod("TryConsume", BindingFlags.Static | BindingFlags.NonPublic)!;
        var resolveDragged = picker.GetMethod(
            "ResolveDraggedObject", BindingFlags.Static | BindingFlags.NonPublic)!;
        var dispatcher = editorAssembly.GetType("BEngine.Editor.GenericMenuDispatcher", throwOnError: true)!;
        var handler = dispatcher.GetProperty("Handler", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousHandler = handler.GetValue(null);

        try
        {
            handler.SetValue(null, BuildObjectPickerCapture(handler.PropertyType));
            Selection.activeObject = spriteTexture;
            OpenSpritePicker(open, token: 71001);
            var items = CapturedObjectPickerItems();
            var useSelected = items.Single(item => ObjectPickerPath(item)
                .StartsWith("Use Selected", StringComparison.OrdinalIgnoreCase));
            Require(ObjectPickerEnabled(useSelected),
                "Sprite ObjectField did not enable Use Selected for a Sprite-mode Texture.");
            Require(items.Any(item => ObjectPickerPath(item)
                        .Equals("Project/Assets/Spark.png", StringComparison.OrdinalIgnoreCase)) &&
                    !items.Any(item => ObjectPickerPath(item)
                        .Equals("Project/Assets/Plain.png", StringComparison.OrdinalIgnoreCase)),
                "Sprite ObjectField dropdown did not filter project images by TextureImporter Sprite mode.");
            InvokeObjectPickerItem(useSelected);
            Require(ConsumeSprite(tryConsume, 71001) is { Texture: "Assets/Spark.png" },
                "Sprite ObjectField Use Selected did not resolve the imported Sprite sub-asset.");

            Selection.activeObject = plainTexture;
            OpenSpritePicker(open, token: 71002);
            items = CapturedObjectPickerItems();
            Require(!ObjectPickerEnabled(items.Single(item => ObjectPickerPath(item)
                        .StartsWith("Use Selected", StringComparison.OrdinalIgnoreCase))),
                "Sprite ObjectField enabled Use Selected for a Texture-mode image.");
            var projectSprite = items.Single(item => ObjectPickerPath(item)
                .Equals("Project/Assets/Spark.png", StringComparison.OrdinalIgnoreCase));
            InvokeObjectPickerItem(projectSprite);
            Require(ConsumeSprite(tryConsume, 71002) is { Texture: "Assets/Spark.png" },
                "Selecting a Sprite-mode image from the ObjectField dropdown did not return its Sprite.");

            DragAndDrop.paths = [];
            DragAndDrop.objectReferences = [spriteTexture];
            Require(ResolveDraggedSprite(resolveDragged) is { Texture: "Assets/Spark.png" },
                "ObjectField did not resolve a dragged Sprite-mode Texture reference as Sprite.");
            DragAndDrop.objectReferences = [];
            DragAndDrop.paths = ["Assets/Spark.png"];
            Require(ResolveDraggedSprite(resolveDragged) is { Texture: "Assets/Spark.png" },
                "ObjectField did not resolve a dragged Sprite-mode image path as Sprite.");

            DragAndDrop.paths = [];
            DragAndDrop.objectReferences = [plainTexture];
            Require(ResolveDraggedSprite(resolveDragged) is null,
                "ObjectField accepted a dragged Texture-mode image reference as Sprite.");
            DragAndDrop.objectReferences = [];
            DragAndDrop.paths = ["Assets/Plain.png"];
            Require(ResolveDraggedSprite(resolveDragged) is null,
                "ObjectField accepted a dragged Texture-mode image path as Sprite.");
        }
        finally
        {
            Selection.activeObject = null;
            DragAndDrop.objectReferences = [];
            DragAndDrop.paths = [];
            handler.SetValue(null, previousHandler);
        }

        return;

        void OpenSpritePicker(MethodInfo method, int token)
        {
            _capturedObjectPickerItems = null;
            method.Invoke(null, [token, new Rect(0, 0, 320, 18), null, typeof(Sprite), false]);
            Require(_capturedObjectPickerItems is not null,
                "Opening a Sprite ObjectField did not produce a dropdown.");
        }
    }

    private static Sprite? ConsumeSprite(MethodInfo tryConsume, int token)
    {
        object?[] arguments = [token, typeof(Sprite), false, null];
        return tryConsume.Invoke(null, arguments) is true ? arguments[3] as Sprite : null;
    }

    private static Sprite? ResolveDraggedSprite(MethodInfo resolveDragged) =>
        resolveDragged.Invoke(null, [typeof(Sprite), false]) as Sprite;

    private static Delegate BuildObjectPickerCapture(Type delegateType)
    {
        var parameter = Expression.Parameter(delegateType.GetMethod("Invoke")!.GetParameters()[0].ParameterType,
            "items");
        var capture = typeof(BAssetTypeSystemTests).GetMethod(nameof(CaptureObjectPickerItems),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return Expression.Lambda(delegateType,
            Expression.Call(capture, Expression.Convert(parameter, typeof(object))), parameter).Compile();
    }

    private static void CaptureObjectPickerItems(object items) => _capturedObjectPickerItems = items;

    private static object[] CapturedObjectPickerItems() =>
        ((IEnumerable?)_capturedObjectPickerItems)?.Cast<object>().ToArray() ?? [];

    private static string ObjectPickerPath(object item) =>
        item.GetType().GetProperty("Path")?.GetValue(item) as string ?? string.Empty;

    private static bool ObjectPickerEnabled(object item) =>
        item.GetType().GetProperty("Enabled")?.GetValue(item) is true;

    private static void InvokeObjectPickerItem(object item)
    {
        var action = item.GetType().GetProperty("Action")?.GetValue(item) as Action;
        Require(action is not null, $"Object picker item '{ObjectPickerPath(item)}' has no action.");
        action!();
    }

    private static void VerifyManagedAssetPersistence()
    {
        var created = ScriptableObject.CreateInstance<ProbeAsset>();
        created.name = "Probe";
        created.value = 7;
        AssetDatabase.CreateAsset(created, "Assets/Probe.asset.yaml");
        var loaded = AssetDatabase.LoadAssetAtPath<ProbeAsset>("Assets/Probe.asset.yaml") ??
                     throw new InvalidOperationException("Managed ScriptableObject did not load.");
        loaded.value = 42;
        EditorUtility.SetDirty(loaded);
        AssetDatabase.SaveAssets();
        var reloaded = AssetDatabase.LoadAssetAtPath<ProbeAsset>("Assets/Probe.asset.yaml");
        Require(reloaded?.value == 42, "Managed BAsset changes were lost after SaveAssets/reload.");
        using var editor = InspectorEditor.CreateEditor(reloaded!);
        Require(editor is BAssetEditor, "ScriptableObject did not resolve the persistent BAssetEditor.");

        var createdMaterial = new Material(Shader.Find("BEngine/Sprite"))
        {
            name = "Created Material",
            color = new Color(0, 1, 0, 1),
            renderQueue = 2100
        };
        AssetDatabase.CreateAsset(createdMaterial, "Assets/Created.material.yaml");
        Require(AssetDatabase.LoadAssetAtPath<Material>("Assets/Created.material.yaml") is
                { renderQueue: 2100 } material && material.color.Equals(new Color(0, 1, 0, 1)),
            "CreateAsset did not use Material.Save schema.");

        var materialMenu = CreateAssetMenuRegistry.entries.Single(entry => entry.AssetType == typeof(Material));
        Require(materialMenu.MenuName == "Rendering/Material" && materialMenu.FileName == "New Material",
            "Material is not exposed through the attributed Assets/Create menu.");
        var menuCreatedPath = ProjectAssetCreation.CreateAttributed("Assets", materialMenu);
        Require(menuCreatedPath.EndsWith("New Material.material.yaml", StringComparison.Ordinal) &&
                AssetDatabase.LoadAssetAtPath<Material>(menuCreatedPath) is not null,
            "Assets/Create Material did not create a typed .material.yaml asset.");

        var persistedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Test.material.yaml")!;
        persistedMaterial.renderQueue = 9999;
        EditorUtility.SetDirty(persistedMaterial);
        Require(AssetDatabase.RevertAsset(persistedMaterial) && persistedMaterial.renderQueue == 2450,
            "BAsset Revert reused the modified cached instance instead of reloading the file.");
    }

    private static void VerifySubAssetPersistence(ProjectWorkspace workspace, ProjectAssetDatabase projectAssets)
    {
        var ownerPath = "Assets/SubOwner.material.yaml";
        AssetDatabase.CreateAsset(new Material(Shader.Find("BEngine/Sprite")) { name = "Sub Owner" }, ownerPath);
        var owner = AssetDatabase.LoadAssetAtPath<Material>(ownerPath)!;
        var child = ScriptableObject.CreateInstance<ProbeAsset>();
        child.name = "Embedded Probe";
        child.value = 17;
        AssetDatabase.AddObjectToAsset(child, owner);
        Require(AssetDatabase.IsMainAsset(owner) && AssetDatabase.IsSubAsset(child) &&
                !AssetDatabase.IsMainAsset(child) && AssetDatabase.Contains(child),
            "AddObjectToAsset did not establish main/sub-asset identity.");
        Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(child, out var guid, out var localId) &&
                guid == AssetDatabase.AssetPathToGUID(ownerPath) && localId > 0 &&
                AssetDatabase.GetAssetPath(child) == ownerPath,
            "A sub-asset did not expose its owner's GUID, stable local identifier, and main asset path.");

        var loaded = AssetDatabase.LoadAllAssetsAtPath(ownerPath).OfType<ProbeAsset>().Single();
        Require(loaded.value == 17 && AssetDatabase.IsSubAsset(loaded),
            "LoadAllAssetsAtPath did not restore an embedded sub-asset.");
        loaded.value = 29;
        EditorUtility.SetDirty(loaded);
        Require(AssetDatabase.SaveAsset(loaded) &&
                AssetDatabase.LoadAllAssetsAtPath(ownerPath).OfType<ProbeAsset>().Single().value == 29,
            "Saving an embedded sub-asset did not persist its serialized state.");

        var referenceOwner = new AssetReferenceProbe { data =
            AssetDatabase.LoadAllAssetsAtPath(ownerPath).OfType<ProbeAsset>().Single() };
        var serializedReference = ComponentFieldSerializer.Serialize(referenceOwner);
        Require(serializedReference[nameof(AssetReferenceProbe.data)].StartsWith("guid:",
                    StringComparison.OrdinalIgnoreCase) &&
                serializedReference[nameof(AssetReferenceProbe.data)].Contains("#subasset=", StringComparison.Ordinal),
            "ComponentFieldSerializer did not persist a stable GUID + localId sub-asset reference.");
        const string movedOwnerPath = "Assets/SubOwnerMoved.material.yaml";
        Require(AssetDatabase.MoveAsset(ownerPath, movedOwnerPath).Length == 0,
            "Moving a main asset that owns embedded sub-assets failed.");
        var restoredReference = new AssetReferenceProbe();
        ComponentFieldSerializer.Deserialize(restoredReference, serializedReference);
        Require(restoredReference.data is { value: 29 } &&
                AssetDatabase.GetAssetPath(restoredReference.data) == movedOwnerPath,
            "A GUID + localId sub-asset reference did not survive moving its main asset.");

        AssetDatabase.RemoveObjectFromAsset(restoredReference.data!);
        Require(!AssetDatabase.IsSubAsset(restoredReference.data!) &&
                !AssetDatabase.LoadAllAssetsAtPath(movedOwnerPath).OfType<ProbeAsset>().Any(),
            "RemoveObjectFromAsset left an embedded sub-asset in its owner metadata.");
        var replacement = ScriptableObject.CreateInstance<ProbeAsset>();
        replacement.name = "Replacement Probe";
        AssetDatabase.AddObjectToAsset(replacement, movedOwnerPath);
        Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(replacement, out _, out var replacementLocalId) &&
                replacementLocalId > localId,
            "Deleting a sub-asset allowed its local identifier to be reused by another object.");
        AssetDatabase.RemoveObjectFromAsset(replacement);
    }

    private static void VerifyTextureAtlasGuidReferencesAndGeneratedSubAsset(
        ProjectWorkspace workspace,
        ProjectAssetDatabase projectAssets,
        TestEditorHost host)
    {
        const string atlasPath = "Assets/Test.atlas.yaml";
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Spark.png") ??
                     throw new InvalidOperationException("The Sprite-mode texture fixture did not load.");
        Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out var spriteGuid, out var spriteLocalId) &&
                spriteLocalId == 21300000,
            "An imported Sprite did not expose a GUID/local identifier sub-asset identity.");
        var spriteOwner = new AssetReferenceProbe();
        ComponentFieldSerializer.Deserialize(spriteOwner, ComponentFieldSerializer.Serialize(
            new AssetReferenceProbe { sprite = sprite }));
        Require(spriteOwner.sprite is { Texture: "Assets/Spark.png" },
            "An imported Sprite sub-asset did not round-trip through ComponentFieldSerializer.");
        Require(AssetDatabase.LoadAllAssetRepresentationsAtPath("Assets/Spark.png") is [Sprite] &&
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    AssetDatabase.LoadAllAssetRepresentationsAtPath("Assets/Spark.png").Single(),
                    out _, out var representedLocalId) && representedLocalId == spriteLocalId,
            "The Texture exposed an unexpected imported Sprite local identifier.");
        var atlas = AssetDatabase.LoadAssetAtPath<TextureAtlas>(atlasPath)!;
        atlas.Sources = [sprite];
        atlas.Save(Path.Combine(workspace.AssetsPath, "Test.atlas.yaml"));
        AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);
        var reloaded = AssetDatabase.LoadAssetAtPath<TextureAtlas>(atlasPath)!;
        var textureReference = reloaded.Texture;
        Require(AssetTypeRegistry.ResolveImporterType(atlasPath) == typeof(TextureAtlasImporter) &&
                textureReference.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) &&
                textureReference.EndsWith("#subasset=2800000", StringComparison.OrdinalIgnoreCase) &&
                !File.Exists(Path.Combine(workspace.AssetsPath, "Test.png")),
            "TextureAtlasImporter did not store a GUID-referenced PNG under Library.");

        Require(reloaded.Sources is
                [{ Texture: "Assets/Spark.png", LocalIdentifier: 21300000 } persisted] &&
                persisted.OwnerGuid.Equals(spriteGuid, StringComparison.OrdinalIgnoreCase) &&
                reloaded.LoadReferencedSprites() is [{ Texture: "Assets/Spark.png" }],
            "TextureAtlas did not persist its Sprite owner GUID/local identifier reference.");
        var yaml = File.ReadAllText(Path.Combine(workspace.AssetsPath, "Test.atlas.yaml"));
        Require(yaml.Contains(spriteGuid, StringComparison.OrdinalIgnoreCase) &&
                yaml.Contains("localIdentifier: 21300000", StringComparison.OrdinalIgnoreCase) &&
                !yaml.Contains("spriteReferences:", StringComparison.OrdinalIgnoreCase) &&
                !yaml.Contains("version:", StringComparison.OrdinalIgnoreCase),
            "TextureAtlas YAML did not persist Sources as versionless Sprite references.");

        var atlasRecord = projectAssets.GetRecord(atlasPath);
        var outputRecord = projectAssets.assets.SingleOrDefault(record =>
            record.ParentGuid == atlasRecord?.Guid && record.LocalIdentifier == 2800000);
        Require(outputRecord is { IsSubAsset: true, AssetType: nameof(BEngine.Texture) } &&
                outputRecord.AssetPath == atlasPath &&
                outputRecord.SourcePath.StartsWith(workspace.AssetArtifactsPath + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) &&
                outputRecord.SourcePath == outputRecord.ArtifactPath &&
                File.Exists(outputRecord.ArtifactPath) && File.Exists(outputRecord.MetaPath),
            "The generated atlas PNG was not registered as a file-backed TextureAtlas sub-asset.");

        var repeatedImportChanges = new List<ProjectAssetChange>();
        void CaptureRepeatedImport(IReadOnlyList<ProjectAssetChange> changes) => repeatedImportChanges.AddRange(changes);
        projectAssets.assetsChanged += CaptureRepeatedImport;
        try { AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate); }
        finally { projectAssets.assetsChanged -= CaptureRepeatedImport; }
        Require(repeatedImportChanges.Count == 0,
            "A stable TextureAtlas import emitted a false update after its importer normalized the source.");

        var atlasFullPath = Path.Combine(workspace.AssetsPath, "Test.atlas.yaml");
        var outputFullPath = outputRecord!.ArtifactPath;
        var atlasGuid = atlasRecord!.Guid.ToString("N");
        var firstCachedTexture = BAsset.LoadSubAsset<BEngine.Texture>($"guid:{atlasGuid}", 2800000)!;
        File.WriteAllBytes(outputFullPath, PngImageCodec.EncodeRgba(2, 1,
            [255, 0, 0, 255, 0, 255, 0, 255]));
        BAsset.Invalidate(atlasFullPath);
        var parentInvalidatedTexture = BAsset.LoadSubAsset<BEngine.Texture>($"guid:{atlasGuid}", 2800000);
        Require(parentInvalidatedTexture is { width: 2, height: 1 } &&
                !ReferenceEquals(parentInvalidatedTexture, firstCachedTexture),
            "Invalidating a main asset left its file-backed sub-asset cache stale.");

        File.WriteAllBytes(outputFullPath, PngImageCodec.EncodeRgba(3, 1,
            [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255]));
        BAsset.Invalidate(outputFullPath);
        var childInvalidatedTexture = BAsset.LoadSubAsset<BEngine.Texture>($"guid:{atlasGuid}", 2800000);
        Require(childInvalidatedTexture is { width: 3, height: 1 } &&
                !ReferenceEquals(childInvalidatedTexture, parentInvalidatedTexture),
            "Invalidating a file-backed sub-asset left its owner-based lookup cache stale.");
        TextureAtlasBuilder.Build(reloaded, atlasFullPath);
        projectAssets.Refresh();
        outputRecord = projectAssets.assets.Single(record =>
            record.ParentGuid == atlasRecord.Guid && record.LocalIdentifier == 2800000);
        Require(outputRecord.ArtifactPath == outputFullPath && File.Exists(outputFullPath),
            "Rebuilding a TextureAtlas did not retain its deterministic Library artifact path.");

        var duplicateFullPath = Path.Combine(workspace.AssetsPath, "DuplicateAtlasTexture.png");
        File.Copy(outputFullPath, duplicateFullPath);
        var duplicateRejected = false;
        try { AssetDatabase.RegisterFileSubAsset(duplicateFullPath, atlasFullPath, 2800000); }
        catch (InvalidOperationException) { duplicateRejected = true; }
        Require(duplicateRejected && !File.Exists(duplicateFullPath + ".meta"),
            "RegisterFileSubAsset allowed two files to claim the same parent GUID/local identifier.");
        VerifyFileSubAssetCollisionRoots(workspace, outputFullPath, atlasFullPath, atlasGuid);
        VerifyGeneratedArtifactTransactions(workspace, outputFullPath, atlasFullPath, atlasGuid);

        const string movedAtlasPath = "Assets/TestMoved.atlas.yaml";
        host.MoveAssetFailure = (oldAssetPath, newAssetPath) =>
            oldAssetPath.Equals(atlasPath, StringComparison.OrdinalIgnoreCase) &&
            newAssetPath.Equals(movedAtlasPath, StringComparison.OrdinalIgnoreCase)
                ? "Injected TextureAtlas move failure."
                : null;
        var failedMove = AssetDatabase.MoveAsset(atlasPath, movedAtlasPath);
        host.MoveAssetFailure = null;
        Require(failedMove.Length > 0 && File.Exists(atlasFullPath) && File.Exists(outputFullPath) &&
                !File.Exists(Path.Combine(workspace.AssetsPath, "TestMoved.atlas.yaml")),
            "A failed TextureAtlas move changed its source or Library artifact.");

        Require(AssetDatabase.MoveAsset(atlasPath, movedAtlasPath).Length == 0 &&
                AssetDatabase.LoadAssetAtPath<TextureAtlas>(movedAtlasPath) is { } movedAtlas &&
                movedAtlas.Texture == textureReference && File.Exists(outputFullPath),
            "A successful TextureAtlas move did not preserve its generated Texture reference.");
        Require(AssetDatabase.MoveAsset(movedAtlasPath, atlasPath).Length == 0 &&
                AssetDatabase.LoadAssetAtPath<TextureAtlas>(atlasPath) is { } restoredAtlas &&
                restoredAtlas.Texture == textureReference && File.Exists(outputFullPath),
            "Moving a TextureAtlas back changed its stable generated Texture reference.");

        host.DeleteAssetFailure = assetPath => assetPath.Equals(atlasPath, StringComparison.OrdinalIgnoreCase);
        var deleteRejected = !AssetDatabase.DeleteAsset(atlasPath);
        host.DeleteAssetFailure = null;
        Require(deleteRejected && File.Exists(atlasFullPath) && File.Exists(outputFullPath),
            "A rejected TextureAtlas deletion removed its Library artifact.");

        var representations = AssetDatabase.LoadAllAssetRepresentationsAtPath(atlasPath);
        var generatedTexture = representations.OfType<BEngine.Texture>().Single();
        Require(AssetDatabase.GetAssetPath(generatedTexture) == atlasPath &&
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(generatedTexture,
                    out var generatedGuid, out var generatedLocalId) &&
                generatedGuid == atlasRecord!.Guid.ToString("N") && generatedLocalId == 2800000,
            "LoadAllAssetsAtPath did not expose the generated PNG with its Atlas GUID/local identifier.");
        var serializedTextureReference = ComponentFieldSerializer.Serialize(
            new AssetReferenceProbe { abstractAsset = generatedTexture });
        Require(serializedTextureReference[nameof(AssetReferenceProbe.abstractAsset)] ==
                $"guid:{atlasGuid}#subasset=2800000",
            "A file-backed sub-asset was not serialized using its main GUID and local identifier.");
        Require(AssetDatabase.MoveAsset(atlasPath, movedAtlasPath).Length == 0,
            "Moving an Atlas before restoring its serialized Texture sub-asset failed.");
        var restoredTextureReference = new AssetReferenceProbe();
        ComponentFieldSerializer.Deserialize(restoredTextureReference, serializedTextureReference);
        Require(restoredTextureReference.abstractAsset is BEngine.Texture { width: > 0, height: > 0 } &&
                AssetDatabase.GetAssetPath(restoredTextureReference.abstractAsset) == movedAtlasPath,
            "A file-backed GUID + localId reference did not survive moving its main asset.");
        Require(AssetDatabase.MoveAsset(movedAtlasPath, atlasPath).Length == 0,
            "Moving the Atlas back after restoring its Texture sub-asset failed.");

        BAsset.ClearLoadedAssets();
        var directTexture = BAsset.Load<BEngine.Texture>(textureReference);
        Require(directTexture is not null && directTexture.assetPath == atlasPath &&
                directTexture.parentAssetGuid == atlasRecord.Guid &&
                directTexture.localIdentifier == 2800000 &&
                directTexture.artifactPath == outputFullPath,
            "Loading a generated Texture reference did not retain its owner/localId and Library artifact.");

        using var packages = new BPackageManager(workspace, catalog: null, loadAssemblies: false);
        var visibleItems = ProjectBrowserTreeBuilder.Build(workspace.AssetsPath, projectAssets.assets.ToArray(), packages);
        var atlasItem = visibleItems.Single(item => item.NormalizedPath.Equals(atlasPath,
            StringComparison.OrdinalIgnoreCase) && !item.IsSubAsset);
        var generatedItems = visibleItems.Where(item => item.IsSubAsset &&
            item.NormalizedPath.Equals(atlasPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        Require(generatedItems is [{ IsSubAsset: true } generated] &&
                generated.BrowserParentKey == atlasItem.BrowserKey &&
                generated.SubAssetObject is BEngine.Texture &&
                generated.BrowserKey != generated.NormalizedPath,
            "ProjectBrowserTreeBuilder did not expose the generated Texture as one child of its atlas owner.");
        using var editor = InspectorEditor.CreateEditor(reloaded);
        Require(editor is TextureAtlasEditor,
            "TextureAtlas did not resolve the Sprite ObjectField-based custom Inspector.");

        var cleanupAtlas = AssetDatabase.LoadAssetAtPath<TextureAtlas>(atlasPath)!;
        cleanupAtlas.Sources = [];
        cleanupAtlas.Save(atlasFullPath);
        AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);
        var clearedAtlas = AssetDatabase.LoadAssetAtPath<TextureAtlas>(atlasPath)!;
        Require(clearedAtlas.Texture.Length == 0 && clearedAtlas.Sprites.Count == 0 &&
                clearedAtlas.Width == 0 && clearedAtlas.Height == 0 &&
                !File.Exists(outputFullPath) && !File.Exists(outputFullPath + ".meta") &&
                !projectAssets.assets.Any(record => record.ParentGuid == atlasRecord.Guid &&
                                                    record.LocalIdentifier == 2800000),
            "TextureAtlasImporter left a stale Library sub-asset after Sources became empty.");
    }

    private static void VerifyFileSubAssetCollisionRoots(
        ProjectWorkspace workspace,
        string sourceTexturePath,
        string ownerPath,
        string ownerGuid)
    {
        var fixtures = new[]
        {
            (Existing: Path.Combine(workspace.AssetsPath, "CollisionInAssets.png"),
                Candidate: Path.Combine(workspace.PackagesPath, "com.test.collision", "Candidate.png"),
                LocalIdentifier: 2800101L),
            (Existing: Path.Combine(workspace.PackagesPath, "com.test.collision", "CollisionInPackages.png"),
                Candidate: Path.Combine(workspace.LibraryPath, "CollisionFixtures", "Candidate.png"),
                LocalIdentifier: 2800102L),
            (Existing: Path.Combine(workspace.LibraryPath, "CollisionFixtures", "CollisionInLibrary.png"),
                Candidate: Path.Combine(workspace.AssetsPath, "CandidateForLibraryCollision.png"),
                LocalIdentifier: 2800103L)
        };
        try
        {
            foreach (var fixture in fixtures)
            {
                WriteFileSubAssetFixture(fixture.Existing, sourceTexturePath, ownerGuid,
                    fixture.LocalIdentifier);
                Directory.CreateDirectory(Path.GetDirectoryName(fixture.Candidate)!);
                File.Copy(sourceTexturePath, fixture.Candidate, overwrite: true);
                var rejected = false;
                try { AssetDatabase.RegisterFileSubAsset(fixture.Candidate, ownerPath, fixture.LocalIdentifier); }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected && !File.Exists(fixture.Candidate + ".meta"),
                    $"File sub-asset collision scanning missed '{fixture.Existing}'.");
            }
        }
        finally
        {
            foreach (var fixture in fixtures)
                foreach (var path in new[] { fixture.Existing, fixture.Existing + ".meta",
                             fixture.Candidate, fixture.Candidate + ".meta" })
                    if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void VerifyGeneratedArtifactTransactions(
        ProjectWorkspace workspace,
        string artifactPath,
        string ownerPath,
        string ownerGuid)
    {
        var collisionPath = Path.Combine(workspace.PackagesPath, "com.test.collision",
            "GeneratedTransactionCollision.png");
        var originalArtifact = File.ReadAllBytes(artifactPath);
        var originalMetadata = File.ReadAllBytes(artifactPath + ".meta");
        try
        {
            WriteFileSubAssetFixture(collisionPath, artifactPath, ownerGuid, 2800000);
            var rejected = false;
            try
            {
                GeneratedAssetArtifacts.Write(ownerPath, 2800000, ".png",
                    PngImageCodec.EncodeRgba(1, 1, [255, 0, 255, 255]));
            }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected && File.ReadAllBytes(artifactPath).SequenceEqual(originalArtifact) &&
                    File.ReadAllBytes(artifactPath + ".meta").SequenceEqual(originalMetadata),
                "A failed generated artifact registration did not restore its previous file pair.");
        }
        finally
        {
            if (File.Exists(collisionPath)) File.Delete(collisionPath);
            if (File.Exists(collisionPath + ".meta")) File.Delete(collisionPath + ".meta");
        }

        var deleteRejected = false;
        using (new FileStream(artifactPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try { GeneratedAssetArtifacts.Delete(ownerPath, 2800000); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                deleteRejected = true;
            }
        }
        Require(deleteRejected && File.ReadAllBytes(artifactPath).SequenceEqual(originalArtifact) &&
                File.ReadAllBytes(artifactPath + ".meta").SequenceEqual(originalMetadata),
            "A partially staged generated artifact deletion did not restore both files.");
    }

    private static void WriteFileSubAssetFixture(
        string path,
        string sourceTexturePath,
        string ownerGuid,
        long localIdentifier)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(sourceTexturePath, path, overwrite: true);
        new BEngine.ProjectSystem.Editor.AssetMetaDocument
        {
            Guid = Guid.NewGuid().ToString("N"),
            Importer = nameof(TextureImporter),
            AssetType = nameof(BEngine.Texture),
            ParentGuid = ownerGuid,
            LocalIdentifier = localIdentifier
        }.Save(path + ".meta");
    }

    private static void VerifyBAssetReferenceRoundTrip()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Test.material.yaml")!;
        var data = AssetDatabase.LoadAssetAtPath<ProbeAsset>("Assets/Probe.asset.yaml")!;
        var scene = new Scene("Reference Round Trip");
        var first = scene.CreateGameObject("First").AddComponent<AssetReferenceProbe>();
        first.material = material;
        first.data = data;
        first.abstractAsset = data;
        first.scriptableAsset = data;
        var second = scene.CreateGameObject("Second").AddComponent<AssetReferenceProbe>();
        second.material = material;
        var firstRenderer = first.gameObject.AddComponent<SpriteRenderer>();
        firstRenderer.material = material;
        var secondRenderer = second.gameObject.AddComponent<SpriteRenderer>();
        secondRenderer.material = material;

        var firstFields = ComponentFieldSerializer.Serialize(first);
        var secondFields = ComponentFieldSerializer.Serialize(second);
        Require(firstFields[nameof(AssetReferenceProbe.abstractAsset) + ".$type"].Contains(
                    typeof(ProbeAsset).FullName!, StringComparison.Ordinal) &&
                firstFields[nameof(AssetReferenceProbe.scriptableAsset) + ".$type"].Contains(
                    typeof(ProbeAsset).FullName!, StringComparison.Ordinal),
            "Abstract BAsset declarations did not write their concrete $type sidecar.");
        Require(!secondFields.ContainsKey(nameof(AssetReferenceProbe.data)) &&
                !secondFields.ContainsKey(nameof(AssetReferenceProbe.abstractAsset)) &&
                !secondFields.ContainsKey(nameof(AssetReferenceProbe.scriptableAsset)),
            "Null BAsset references should not emit empty serialized fields.");

        var restored = SceneAssetSerialization.Deserialize(SceneAssetSerialization.Serialize(scene));
        var probes = restored.QueryComponents<AssetReferenceProbe>();
        Require(probes.Count == 2 && probes[0].material is not null &&
                ReferenceEquals(probes[0].material, probes[1].material) &&
                probes[0].material!.Id == material.Id,
            "Two component Material references did not resolve to one cached asset identity.");
        Require(probes[0].data is { value: 42 } && probes[1].data is null,
            "Custom ScriptableObject or empty BAsset reference did not round-trip.");
        Require(probes[0].abstractAsset is ProbeAsset { value: 42 } &&
                probes[0].scriptableAsset is ProbeAsset { value: 42 },
            "A polymorphic BAsset reference did not load using its concrete $type sidecar.");
        var renderers = restored.QueryComponents<SpriteRenderer>();
        Require(renderers.Count == 2 && ReferenceEquals(renderers[0].material, renderers[1].material) &&
                renderers[0].BatchKeyUnchecked.Equals(renderers[1].BatchKeyUnchecked),
            "Shared Material identity no longer produces equal sprite batch keys.");

        var broken = new AssetReferenceProbe();
        var brokenRenderer = scene.CreateGameObject("Broken Renderer").AddComponent<SpriteRenderer>();
        var defaultMaterial = brokenRenderer.material;
        var warnings = new List<LogEntry>();
        void Capture(LogEntry entry)
        {
            if (entry.Type == LogType.Warning) warnings.Add(entry);
        }
        Debug.MessageLogged += Capture;
        try
        {
            ComponentFieldSerializer.Deserialize(broken, new Dictionary<string, string>
            {
                [nameof(AssetReferenceProbe.material)] = "Assets/Bad.material.yaml",
                [nameof(AssetReferenceProbe.data)] = "Assets/Missing.asset.yaml",
                [nameof(AssetReferenceProbe.abstractAsset)] = "Assets/Probe.asset.yaml",
                [nameof(AssetReferenceProbe.abstractAsset) + ".$type"] = typeof(Material).AssemblyQualifiedName!
            });
            ComponentFieldSerializer.Deserialize(brokenRenderer, new Dictionary<string, string>
            {
                [nameof(SpriteRenderer.material)] = "Assets/Bad.material.yaml"
            });
        }
        finally { Debug.MessageLogged -= Capture; }
        Require(broken.material is null && broken.data is null && broken.abstractAsset is null &&
                ReferenceEquals(brokenRenderer.material, defaultMaterial) && warnings.Count >= 4,
            "Broken BAsset references did not warn while retaining constructor defaults.");
        scene.Dispose();
        restored.Dispose();
    }

    private static void VerifyPackageReference(ProjectWorkspace workspace)
    {
        var packageRoot = Path.Combine(workspace.PackagesPath, "com.test.assets");
        Directory.CreateDirectory(packageRoot);
        new Material(Shader.Find("BEngine/Sprite")) { name = "Package Material" }
            .Save(Path.Combine(packageRoot, "Package.material.yaml"));
        var material = BAsset.Load<Material>("Packages/com.test.assets/Package.material.yaml");
        Require(material?.assetPath == "Packages/com.test.assets/Package.material.yaml",
            "Packages asset reference did not preserve its stable project prefix.");
    }

    private static void VerifyInheritedAndUnloadableIcons(string root)
    {
        var icons = Path.Combine(root, "Editor", "Icons");
        Directory.CreateDirectory(icons);
        var inheritedIcon = Path.Combine(icons, "Probe.png");
        var explicitIcon = Path.Combine(icons, "Explicit.png");
        File.WriteAllBytes(inheritedIcon, [1]);
        File.WriteAllBytes(explicitIcon, [2]);
        EditorResource.RegisterResourceRoot(root);

        Require(EditorIconRegistry.GetIconPath(typeof(DerivedProbeAsset)) == inheritedIcon,
            "EditorIconAttribute did not flow to a ScriptableObject subclass.");
        EditorIconRegistry.Register(typeof(ExplicitIconAsset), "Icons/Explicit.png", useForChildren: true);
        Require(EditorIconRegistry.GetIconPath(typeof(DerivedExplicitIconAsset)) == explicitIcon,
            "Explicit EditorIconRegistry child registration was not resolved.");
        EditorIconRegistry.UnregisterAssembly(typeof(ExplicitIconAsset).Assembly);
        Require(EditorIconRegistry.GetIconPath(typeof(DerivedExplicitIconAsset)) is { } fallbackIcon &&
                fallbackIcon.EndsWith("AssetData.png", StringComparison.OrdinalIgnoreCase),
            "Assembly unload did not restore the inherited ScriptableObject Data icon.");
        Require(EditorIconRegistry.GetIconPath(typeof(DerivedProbeAsset)) == inheritedIcon,
            "Attribute icon did not recover after the dynamic cache was cleared.");
    }

    private static void WriteFixtures(ProjectWorkspace workspace)
    {
        var png = PngImageCodec.EncodeRgba(1, 1, [255, 255, 255, 255]);
        File.WriteAllBytes(Path.Combine(workspace.AssetsPath, "Spark.png"), png);
        File.WriteAllBytes(Path.Combine(workspace.AssetsPath, "Plain.png"), png);
        File.WriteAllBytes(Path.Combine(workspace.AssetsPath, "Unsupported.jpg"), png);
        File.WriteAllBytes(Path.Combine(workspace.AssetsPath, "Test.ttf"), [0, 1, 0, 0]);
        File.WriteAllText(Path.Combine(workspace.AssetsPath, "Probe.cs"),
            "using BEngine; public sealed class Probe : MonoBehaviour { }");
        File.WriteAllText(Path.Combine(workspace.AssetsPath, "Test.shader"),
            "#pragma stage fragment\nvoid main() {}\n");
        using (var assetScene = new Scene("Asset Scene"))
            SceneAssetSerialization.Save(assetScene,
                Path.Combine(workspace.AssetsPath, "Test.scene.yaml"));
        new Material(Shader.Find("BEngine/Sprite"))
        {
            name = "Test Material",
            color = new Color(1, 0, 0, 1),
            renderQueue = 2450
        }.Save(Path.Combine(workspace.AssetsPath, "Test.material.yaml"));
        File.WriteAllText(Path.Combine(workspace.AssetsPath, "Spark.png.meta"), """
            format: BEngine.AssetMeta
            version: 1
            guid: 2aac6de523664701b4988048243887bd
            importer: TextureImporter
            assetType: Texture
            sourceHash: ''
            settings:
              textureType: Sprite
              spritePivotX: '0.5'
              spritePivotY: '0.5'
            """);
        new TextureAtlas { name = "Test Atlas" }
            .Save(Path.Combine(workspace.AssetsPath, "Test.atlas.yaml"));
        new AnimationClip { name = "Test Clip" }
            .Save(Path.Combine(workspace.AssetsPath, "Test.anim.yaml"));
        new AnimatorController { name = "Test Controller" }
            .Save(Path.Combine(workspace.AssetsPath, "Test.controller.yaml"));
        YamlUtility.Save(new ExternalYamlAsset { name = "External", strength = 9 },
            Path.Combine(workspace.AssetsPath, "Test.external.yaml"));
        File.WriteAllText(Path.Combine(workspace.AssetsPath, "Bad.material.yaml"),
            "format: BEngine.Material\nversion: [\n");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

[EditorIcon("Icons/Probe.png")]
internal class ProbeAsset : ScriptableObject
{
    public int value { get; set; }
}

internal sealed class DerivedProbeAsset : ProbeAsset;
internal class ExplicitIconAsset : ScriptableObject;
internal sealed class DerivedExplicitIconAsset : ExplicitIconAsset;

public sealed class ExternalYamlAsset : ScriptableObject
{
    public int strength { get; set; }
}

internal sealed class AssetReferenceProbe : MonoBehaviour
{
    public AssetReferenceProbe() { }
    public Material? material { get; set; }
    public ProbeAsset? data { get; set; }
    public BAsset? abstractAsset { get; set; }
    public ScriptableObject? scriptableAsset { get; set; }
    public Sprite? sprite { get; set; }
}
