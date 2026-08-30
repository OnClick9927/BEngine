using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Editor.Rendering;
using EditorAssetPreview = BEngine.Editor.AssetPreview;

namespace BEngine.ExampleTests.AssetPreview;

internal static class Program
{
    private const int WideWidth = 500;
    private const int WideHeight = 520;
    private const int NarrowWidth = 260;
    private const int NarrowHeight = 420;

    private static int Main()
    {
        try
        {
            EditorAppearance.Apply(new EditorPreferencesDocument());
            using var fixture = new AssetPreviewFixture();
            var texture = fixture.CreateTexture("WidePreview", 200, 100,
                new Color(Fix64.FromDecimal(0.2m), Fix64.FromDecimal(0.7m),
                    Fix64.FromDecimal(0.9m), 1));
            var text = fixture.CreateText("PreviewNotes",
                "TEXT_SELECTION_SENTINEL\nSecond preview line\nThird preview line");
            var (atlas, atlasTexturePath) = fixture.CreateAtlas();
            var material = new Material(Shader.Find("BEngine/PreviewRegression"))
            {
                name = "Preview Material",
                color = new Color(Fix64.FromDecimal(0.13m), Fix64.FromDecimal(0.42m),
                    Fix64.FromDecimal(0.77m), Fix64.FromDecimal(0.61m))
            };
            var missing = fixture.CreateMissingTexture();
            var bitmap = fixture.CreateBitmap("BitmapPreview", 90, 45,
                new Color(Fix64.FromDecimal(0.8m), Fix64.FromDecimal(0.2m),
                    Fix64.FromDecimal(0.35m), 1));
            var jpeg = fixture.CreateJpeg("JpegPreview", 80, 120,
                new Color(Fix64.FromDecimal(0.22m), Fix64.FromDecimal(0.75m),
                    Fix64.FromDecimal(0.4m), 1));
            var runtimeText = new BEngine.TextAsset(
                "RUNTIME_TEXT_ASSET_SENTINEL\nSecond runtime line", "RuntimeNotes.txt");
            var shader = Shader.Find("BEngine/TypedPreviewRegression");
            using var scene = new Scene("Typed Preview Scene");
            scene.CreateGameObject("Preview Root A");
            scene.CreateGameObject("Preview Root B");

            using var inspector = new InspectorHarness(texture, texture.sourcePath);
            VerifyTexturePreview(inspector, texture);
            VerifyPreviewHeightSplitter(inspector, texture);
            VerifySelectionSwitchAndLock(inspector, texture, text);
            VerifyInspectorLifecycle(inspector, texture, text);
            VerifyTextCacheRefresh(fixture, inspector, text);
            VerifyAtlasPreview(inspector, atlas, atlasTexturePath);
            VerifyMaterialPreview(inspector, material);
            VerifyTypedBAssetPreviews(inspector, runtimeText, shader, scene);
            VerifyImportedRasterPreviews(inspector, bitmap, jpeg);
            VerifyMissingTextureFallback(inspector, missing);
            VerifyTextureCacheRefresh(fixture, inspector, texture);
            VerifyAtlasTextureCacheRefresh(fixture, inspector, atlas, atlasTexturePath);
            VerifyCustomEditorPreviewProtocol(inspector, texture);
            VerifyNarrowInspector(inspector,
                [(texture, texture.sourcePath), (text, text.sourcePath), (atlas, atlas.sourcePath),
                    (material, string.Empty), (missing, missing.sourcePath)]);
            GpuPreviewCacheLifecycleTests.Run();

            Console.WriteLine("ASSET_PREVIEW_OK|texture-fit,text-switch,editor-lifecycle,inspector-reopen," +
                              "locked-inspector,locked-forced-rebuild,locked-reopen,text-cache-refresh," +
                              "atlas-generated-png,material-swatch,jpeg-bmp,missing-fallback,texture-cache-refresh," +
                              "atlas-texture-cache-refresh,typed-basset-previews,custom-editor-preview-protocol," +
                              "preview-fault-isolation,preview-height-splitter,narrow-clipping," +
                              "gpu-revision-lifecycle,gpu-cache-capacity," +
                              "gpu-fence-retirement");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ASSET_PREVIEW_FAILED|{exception}");
            return 1;
        }
        finally
        {
            EditorAssetPreview.ClearTemporaryAssetPreviews();
            GUIUtility.hotControl = 0;
            GUIUtility.keyboardControl = 0;
        }
    }

    private static void VerifyTexturePreview(InspectorHarness inspector, DefaultAsset texture)
    {
        var commands = inspector.Repaint(WideWidth, WideHeight);
        var header = TestAssert.Text(commands, "Preview");
        TestAssert.Require(header.Rect.Y > WideHeight / 2f,
            "The asset preview was not anchored to the bottom of the Inspector.");
        var image = TestAssert.PreviewImage(commands, texture.sourcePath);
        TestAssert.Require(Math.Abs(image.Rect.Width / image.Rect.Height - 2f) < 0.02f,
            $"The 2:1 texture preview was distorted to {image.Rect.Width}:{image.Rect.Height}.");
        TestAssert.Require(image.Rect.Width > 100 && image.Rect.Y >= header.Rect.Bottom,
            "The test found only the header thumbnail, not the full texture preview.");
        TestAssert.Require(image.Rect.X >= image.ClipRect.X - 0.1f &&
                           image.Rect.Right <= image.ClipRect.Right + 0.1f,
            "The fitted texture escaped the preview ClipRect.");
    }

    private static void VerifyPreviewHeightSplitter(InspectorHarness inspector, DefaultAsset texture)
    {
        var initial = TestAssert.Text(inspector.Repaint(WideWidth, WideHeight), "Preview").Rect.Y;
        var start = new Vector2(120, (Fix64)initial);
        var expanded = new Vector2(start.x, start.y - 72);
        inspector.Dispatch(new Event(EventType.MouseDown) { mousePosition = start, button = 0 },
            WideWidth, WideHeight);
        inspector.Dispatch(new Event(EventType.MouseDrag)
        {
            mousePosition = expanded,
            delta = expanded - start,
            button = 0
        }, WideWidth, WideHeight);
        inspector.Dispatch(new Event(EventType.MouseUp) { mousePosition = expanded, button = 0 },
            WideWidth, WideHeight);

        var expandedCommands = inspector.Repaint(WideWidth, WideHeight);
        var expandedHeader = TestAssert.Text(expandedCommands, "Preview").Rect.Y;
        TestAssert.Require(expandedHeader < initial - 60,
            "Dragging the Inspector preview splitter upward did not increase the preview height.");
        TestAssert.PreviewImage(expandedCommands, texture.sourcePath);
        TestAssert.Require(GUIUtility.hotControl == 0,
            "The Inspector preview splitter did not release its hot control.");

        var shrinkStart = new Vector2(120, (Fix64)expandedHeader);
        var shrinkEnd = new Vector2(shrinkStart.x, shrinkStart.y + 1000);
        inspector.Dispatch(new Event(EventType.MouseDown) { mousePosition = shrinkStart, button = 0 },
            WideWidth, WideHeight);
        inspector.Dispatch(new Event(EventType.MouseDrag)
        {
            mousePosition = shrinkEnd,
            delta = shrinkEnd - shrinkStart,
            button = 0
        }, WideWidth, WideHeight);
        inspector.Dispatch(new Event(EventType.MouseUp) { mousePosition = shrinkEnd, button = 0 },
            WideWidth, WideHeight);

        var shrunkenCommands = inspector.Repaint(WideWidth, WideHeight);
        var shrunkenHeader = TestAssert.Text(shrunkenCommands, "Preview").Rect.Y;
        TestAssert.Require(shrunkenHeader > expandedHeader + 60,
            "Dragging the Inspector preview splitter downward did not reduce the preview height.");
        TestAssert.Require(shrunkenHeader <= WideHeight - 118,
            "The Inspector preview splitter allowed the preview body to become unreadably short.");
        TestAssert.PreviewImage(shrunkenCommands, texture.sourcePath);
        TestAssert.Require(GUIUtility.hotControl == 0,
            "The Inspector preview splitter retained hot control after a clamped drag.");

        var restoreStart = new Vector2(120, (Fix64)shrunkenHeader);
        var restoreEnd = new Vector2(restoreStart.x,
            restoreStart.y - (Fix64)(shrunkenHeader - initial));
        inspector.Dispatch(new Event(EventType.MouseDown) { mousePosition = restoreStart, button = 0 },
            WideWidth, WideHeight);
        inspector.Dispatch(new Event(EventType.MouseDrag)
        {
            mousePosition = restoreEnd,
            delta = restoreEnd - restoreStart,
            button = 0
        }, WideWidth, WideHeight);
        inspector.Dispatch(new Event(EventType.MouseUp) { mousePosition = restoreEnd, button = 0 },
            WideWidth, WideHeight);
        var restoredHeader = TestAssert.Text(inspector.Repaint(WideWidth, WideHeight), "Preview").Rect.Y;
        TestAssert.Require(Math.Abs(restoredHeader - initial) < 1,
            "The Inspector preview splitter did not retain an explicitly restored height.");
    }

    private static void VerifySelectionSwitchAndLock(InspectorHarness inspector, DefaultAsset texture,
        BEngine.Editor.TextAsset text)
    {
        var textureEditor = inspector.CurrentEditor;
        inspector.IsLocked = true;
        inspector.Select(text, text.sourcePath);
        var locked = inspector.Repaint(WideWidth, WideHeight);
        TestAssert.Require(ReferenceEquals(textureEditor, inspector.CurrentEditor),
            "A locked Inspector replaced its asset Editor after Selection changed.");
        TestAssert.PreviewImage(locked, texture.sourcePath);
        TestAssert.Require(!locked.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                  command.Content.Contains("TEXT_SELECTION_SENTINEL",
                                                      StringComparison.Ordinal)),
            "A locked Inspector displayed the newly selected TextAsset.");

        inspector.IsLocked = false;
        var textEditor = inspector.CurrentEditor;
        TestAssert.Require(!ReferenceEquals(textureEditor, textEditor) &&
                           !InspectorHarness.IsEnabled(textureEditor) &&
                           ReferenceEquals(textEditor.target, text),
            "Unlocking did not immediately dispose the frozen Editor and rebuild from Selection.");
        var textCommands = inspector.Repaint(WideWidth, WideHeight);
        TestAssert.Require(textCommands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                       command.Content.Contains("TEXT_SELECTION_SENTINEL",
                                                           StringComparison.Ordinal)),
            "The TextAsset preview did not render its source content.");
        TestAssert.Require(!textCommands.Any(command => command.Type == GpuCanvasCommandType.Image &&
                                                       command.Content.StartsWith(texture.sourcePath,
                                                           StringComparison.OrdinalIgnoreCase)),
            "The TextAsset preview retained the previous texture image command.");

        inspector.Select(texture, texture.sourcePath);
        var textureCommands = inspector.Repaint(WideWidth, WideHeight);
        TestAssert.Require(!ReferenceEquals(textEditor, inspector.CurrentEditor) &&
                           !InspectorHarness.IsEnabled(textEditor),
            "Switching back to PNG did not dispose and replace the TextAsset Editor.");
        TestAssert.PreviewImage(textureCommands, texture.sourcePath);
    }

    private static void VerifyInspectorLifecycle(InspectorHarness inspector, DefaultAsset texture,
        BEngine.Editor.TextAsset otherSelection)
    {
        inspector.Select(texture, texture.sourcePath);
        inspector.Repaint(WideWidth, WideHeight);
        var beforeClose = inspector.CurrentEditor;
        inspector.CloseAndReopen();
        TestAssert.Require(!InspectorHarness.IsEnabled(beforeClose),
            "Closing the Inspector did not dispose its asset Editor.");
        TestAssert.PreviewImage(inspector.Repaint(WideWidth, WideHeight), texture.sourcePath);
        TestAssert.Require(!ReferenceEquals(beforeClose, inspector.CurrentEditor) &&
                           InspectorHarness.IsEnabled(inspector.CurrentEditor),
            "Reopening the Inspector with the same Selection did not rebuild its asset Editor.");

        inspector.IsLocked = true;
        var beforeReload = inspector.CurrentEditor;
        inspector.Select(otherSelection, otherSelection.sourcePath);
        inspector.RebuildEditor(force: true);
        TestAssert.Require(!ReferenceEquals(beforeReload, inspector.CurrentEditor) &&
                           !InspectorHarness.IsEnabled(beforeReload) &&
                           ReferenceEquals(inspector.CurrentEditor.target, texture),
            "A forced rebuild did not refresh the locked Editor while preserving its target.");
        TestAssert.PreviewImage(inspector.Repaint(WideWidth, WideHeight), texture.sourcePath);
        var lockedEditor = inspector.CurrentEditor;
        inspector.CloseAndReopen();
        TestAssert.Require(!InspectorHarness.IsEnabled(lockedEditor) && inspector.IsLocked,
            "Closing a locked Inspector did not dispose its Editor or unexpectedly cleared the lock.");
        TestAssert.PreviewImage(inspector.Repaint(WideWidth, WideHeight), texture.sourcePath);
        TestAssert.Require(ReferenceEquals(inspector.CurrentEditor.target, texture),
            "Reopening a locked Inspector silently changed it to the current Selection.");
        inspector.IsLocked = false;
    }

    private static void VerifyTextCacheRefresh(AssetPreviewFixture fixture, InspectorHarness inspector,
        BEngine.Editor.TextAsset text)
    {
        fixture.RewriteText(text.sourcePath,
            "TEXT_REFRESH_SENTINEL\nUpdated externally while the asset remains selected");
        inspector.Select(text, text.sourcePath);
        var commands = inspector.Repaint(WideWidth, WideHeight);
        TestAssert.Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                   command.Content.Contains("TEXT_REFRESH_SENTINEL",
                                                       StringComparison.Ordinal)),
            "The selected TextAsset preview retained the Selection-time source after an external edit.");
        TestAssert.Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                   command.Content.StartsWith("Text | 2 lines",
                                                       StringComparison.Ordinal)),
            "The TextAsset preview info retained stale line metadata after an external edit.");
    }

    private static void VerifyAtlasPreview(InspectorHarness inspector, DefaultAsset atlas,
        string atlasTexturePath)
    {
        inspector.Select(atlas, atlas.sourcePath);
        var commands = inspector.Repaint(WideWidth, WideHeight);
        var image = TestAssert.PreviewImage(commands, atlasTexturePath);
        TestAssert.Require(image.Rect.Width > 100,
            "The atlas Inspector did not render its generated PNG in the preview body.");
        TestAssert.Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                   command.Content.Contains("2 sprites",
                                                       StringComparison.OrdinalIgnoreCase)),
            "The atlas preview info does not report its sprite count.");
    }

    private static void VerifyMaterialPreview(InspectorHarness inspector, Material material)
    {
        inspector.Select(material, string.Empty);
        var commands = inspector.Repaint(WideWidth, WideHeight);
        var swatchColor = GpuCanvasColor.FromColor(material.color);
        TestAssert.Require(commands.Any(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                                   command.Color == swatchColor &&
                                                   command.Rect.Width > 100 && command.Rect.Height > 40),
            "The Material preview did not draw its color as a programmatic swatch.");
        TestAssert.Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                   command.Content == material.shader.shaderName),
            "The Material preview did not identify its Shader.");
    }

    private static void VerifyTypedBAssetPreviews(InspectorHarness inspector, BEngine.TextAsset text,
        Shader shader, Scene scene)
    {
        inspector.Select(text, text.path);
        var textCommands = inspector.Repaint(WideWidth, WideHeight);
        TestAssert.Require(textCommands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                       command.Content.Contains("RUNTIME_TEXT_ASSET_SENTINEL",
                                                           StringComparison.Ordinal)),
            "The runtime TextAsset did not render its text preview.");

        inspector.Select(shader, string.Empty);
        var shaderCommands = inspector.Repaint(WideWidth, WideHeight);
        TestAssert.Require(shaderCommands.Any(command => command.Type == GpuCanvasCommandType.GradientRect) &&
                           shaderCommands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                         command.Content == shader.shaderName),
            "The Shader BAsset did not render its distinct shader preview.");

        inspector.Select(scene, string.Empty);
        var sceneCommands = inspector.Repaint(WideWidth, WideHeight);
        TestAssert.Require(sceneCommands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                        command.Content.Contains("2 objects",
                                                            StringComparison.OrdinalIgnoreCase)) &&
                           sceneCommands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                        command.Content.Contains("2 GameObjects",
                                                            StringComparison.Ordinal)),
            "The Scene BAsset preview did not report its object and root summary.");
    }

    private static void VerifyImportedRasterPreviews(InspectorHarness inspector, params DefaultAsset[] assets)
    {
        foreach (var asset in assets)
        {
            var preview = RequiredPreview(asset);
            TestAssert.Require(FileGpuCanvasResourceResolver.Shared.TryResolveTexture(preview.Source, out var data) &&
                               data.Width == preview.Width && data.Height == preview.Height &&
                               data.Pixels.Length == checked(preview.Width * preview.Height * 4),
                $"The GPU resource resolver did not decode the {Path.GetExtension(asset.sourcePath)} preview.");
            inspector.Select(asset, asset.sourcePath);
            var image = TestAssert.PreviewImage(inspector.Repaint(WideWidth, WideHeight), asset.sourcePath);
            TestAssert.Require(Math.Abs(image.Rect.Width / image.Rect.Height -
                                        (float)preview.Width / preview.Height) < 0.02f,
                $"The {Path.GetExtension(asset.sourcePath)} Inspector preview distorted its aspect ratio.");
        }
    }

    private static void VerifyMissingTextureFallback(InspectorHarness inspector, DefaultAsset missing)
    {
        inspector.Select(missing, missing.sourcePath);
        var commands = inspector.Repaint(WideWidth, WideHeight);
        TestAssert.Require(!commands.Any(command => command.Type == GpuCanvasCommandType.Image &&
                                                   command.Content.StartsWith(missing.sourcePath,
                                                       StringComparison.OrdinalIgnoreCase)),
            "The missing texture path was submitted as an Image command.");
        TestAssert.Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                   command.Content == "Image preview unavailable"),
            "The missing texture did not render the fallback preview.");
        TestAssert.Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                   command.Content.Contains("Preview unavailable",
                                                       StringComparison.Ordinal)),
            "The missing texture info did not explain that its preview is unavailable.");
    }

    private static void VerifyTextureCacheRefresh(AssetPreviewFixture fixture, InspectorHarness inspector,
        DefaultAsset texture)
    {
        var initial = RequiredPreview(texture);
        fixture.RewritePng(texture.sourcePath, 75, 150,
            new Color(Fix64.FromDecimal(0.91m), Fix64.FromDecimal(0.24m),
                Fix64.FromDecimal(0.38m), 1));
        var refreshed = RequiredPreview(texture);
        TestAssert.Require(refreshed.Width == 75 && refreshed.Height == 150,
            $"AssetPreview retained stale PNG dimensions {refreshed.Width}x{refreshed.Height}.");
        TestAssert.Require(refreshed.Source != initial.Source &&
                           refreshed.Source.Contains("?bengine-preview=", StringComparison.Ordinal),
            "AssetPreview did not update the PNG source revision after an in-place rewrite.");

        inspector.Select(texture, texture.sourcePath);
        var commands = inspector.Repaint(WideWidth, WideHeight);
        var image = TestAssert.PreviewImage(commands, texture.sourcePath);
        TestAssert.Require(image.Content == refreshed.Source &&
                           Math.Abs(image.Rect.Width / image.Rect.Height - 0.5f) < 0.02f,
            "Inspector retained the stale source revision or aspect ratio after a PNG rewrite.");
        TestAssert.Require(!commands.Any(command => command.Type == GpuCanvasCommandType.Image &&
                                                   command.Content == initial.Source),
            "Inspector emitted the previous cached PNG revision after the file changed.");
        TestAssert.Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                   command.Content.StartsWith("75 x 150 | PNG",
                                                       StringComparison.Ordinal)),
            "Inspector info retained the stale PNG dimensions after the file changed.");
    }

    private static void VerifyAtlasTextureCacheRefresh(AssetPreviewFixture fixture,
        InspectorHarness inspector, DefaultAsset atlas, string atlasTexturePath)
    {
        var initial = RequiredPreview(atlas);
        var manifestBytes = File.ReadAllBytes(atlas.sourcePath);
        var manifestWriteTime = File.GetLastWriteTimeUtc(atlas.sourcePath);
        fixture.RewritePng(atlasTexturePath, 96, 32,
            new Color(Fix64.FromDecimal(0.18m), Fix64.FromDecimal(0.86m),
                Fix64.FromDecimal(0.31m), 1));
        TestAssert.Require(File.ReadAllBytes(atlas.sourcePath).SequenceEqual(manifestBytes) &&
                           File.GetLastWriteTimeUtc(atlas.sourcePath) == manifestWriteTime,
            "The atlas cache fixture unexpectedly modified the manifest.");

        var refreshed = RequiredPreview(atlas);
        TestAssert.Require(refreshed.Width == 96 && refreshed.Height == 32,
            $"AssetPreview retained stale atlas PNG dimensions {refreshed.Width}x{refreshed.Height}.");
        TestAssert.Require(refreshed.Source != initial.Source,
            "AssetPreview did not update the atlas texture revision when only its PNG changed.");

        inspector.Select(atlas, atlas.sourcePath);
        var commands = inspector.Repaint(WideWidth, WideHeight);
        var image = TestAssert.PreviewImage(commands, atlasTexturePath);
        TestAssert.Require(image.Content == refreshed.Source &&
                           Math.Abs(image.Rect.Width / image.Rect.Height - 3f) < 0.02f,
            "Inspector retained the stale atlas texture revision or size while the manifest stayed unchanged.");
        TestAssert.Require(!commands.Any(command => command.Type == GpuCanvasCommandType.Image &&
                                                   command.Content == initial.Source),
            "Inspector emitted the previous cached atlas PNG revision after the texture changed.");
    }

    private static void VerifyCustomEditorPreviewProtocol(InspectorHarness inspector, DefaultAsset nextAsset)
    {
        PreviewProtocolProbeEditor.Reset();
        FaultingPreviewProbeEditor.Reset();
        var probe = ScriptableObject.CreateInstance<PreviewProtocolProbeAsset>();
        probe.name = "Custom Preview Protocol";
        inspector.Select(probe, string.Empty);
        var commands = inspector.Repaint(WideWidth, WideHeight);
        TestAssert.Require(inspector.CurrentEditor is PreviewProtocolProbeEditor,
            "Inspector did not resolve the ProbeAsset [CustomEditor].");
        TestAssert.Require(PreviewProtocolProbeEditor.PreviewCalls > 0 &&
                           commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                   command.Content == PreviewProtocolProbeEditor.PreviewSentinel),
            "Inspector did not invoke the custom Editor.OnPreviewGUI protocol.");
        TestAssert.Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                   command.Content == PreviewProtocolProbeEditor.InfoSentinel),
            "Inspector did not render the custom Editor.GetInfoString result.");

        var faulting = ScriptableObject.CreateInstance<FaultingPreviewProbeAsset>();
        faulting.name = "Faulting Preview Protocol";
        inspector.Select(faulting, string.Empty);
        var faultCommands = inspector.Repaint(WideWidth, WideHeight);
        TestAssert.Require(PreviewProtocolProbeEditor.DisableCalls == 1,
            $"Custom preview Editor OnDisable ran {PreviewProtocolProbeEditor.DisableCalls} times after switching.");
        TestAssert.Require(inspector.CurrentEditor is FaultingPreviewProbeEditor &&
                           FaultingPreviewProbeEditor.PreviewCalls > 0,
            "Inspector did not invoke the faulting custom preview Editor.");
        var header = TestAssert.Text(faultCommands, "Preview");
        TestAssert.Require(faultCommands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                        command.Content == FaultingPreviewProbeEditor.InfoSentinel),
            "A faulting OnPreviewGUI prevented the Inspector from rendering preview info.");
        TestAssert.Require(faultCommands.Any(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                                        Math.Abs(command.Rect.X - 4) < 0.1f &&
                                                        Math.Abs(command.Rect.Width - (WideWidth - 8)) < 0.1f &&
                                                        command.Rect.Y >= header.Rect.Bottom &&
                                                        command.Rect.Height > 30),
            "A faulting OnPreviewGUI removed the Inspector preview frame.");

        inspector.Repaint(WideWidth, WideHeight);
        TestAssert.Require(PreviewProtocolProbeEditor.DisableCalls == 1,
            "Repeated repaint invoked OnDisable again for the already replaced custom Editor.");
        inspector.Select(nextAsset, nextAsset.sourcePath);
        TestAssert.PreviewImage(inspector.Repaint(WideWidth, WideHeight), nextAsset.sourcePath);
        TestAssert.Require(FaultingPreviewProbeEditor.DisableCalls == 1,
            "Inspector did not dispose the faulting preview Editor exactly once when Selection changed.");
    }

    private static void VerifyNarrowInspector(InspectorHarness inspector,
        IReadOnlyList<(BObject Asset, string Path)> assets)
    {
        foreach (var (asset, path) in assets)
        {
            inspector.Select(asset, path);
            var commands = inspector.Repaint(NarrowWidth, NarrowHeight);
            var header = TestAssert.Text(commands, "Preview");
            foreach (var command in commands)
            {
                TestAssert.Require(IsFinite(command.Rect) && IsFinite(command.ClipRect) &&
                                   command.Rect.Width >= 0 && command.Rect.Height >= 0 &&
                                   command.ClipRect.Width >= 0 && command.ClipRect.Height >= 0,
                    $"{asset.name} emitted an invalid preview/layout command.");
                TestAssert.Require(command.ClipRect.X >= -0.1f && command.ClipRect.Y >= -0.1f &&
                                   command.ClipRect.Right <= NarrowWidth + 0.1f &&
                                   command.ClipRect.Bottom <= NarrowHeight + 0.1f,
                    $"{asset.name} emitted a ClipRect outside the 260px Inspector.");

                var visible = command.Rect.Right > command.ClipRect.X &&
                              command.Rect.X < command.ClipRect.Right &&
                              command.Rect.Bottom > command.ClipRect.Y &&
                              command.Rect.Y < command.ClipRect.Bottom;
                if (!visible || command.Rect.Y < header.Rect.Y - 0.1f) continue;
                TestAssert.Require(command.Rect.X >= -0.1f && command.Rect.Right <= NarrowWidth + 0.1f &&
                                   command.Rect.Y >= header.Rect.Y - 0.1f &&
                                   command.Rect.Bottom <= NarrowHeight + 0.1f,
                    $"{asset.name} preview escaped the narrow Inspector window.");
            }
        }
    }

    private static bool IsFinite(GpuCanvasRect rect) =>
        float.IsFinite(rect.X) && float.IsFinite(rect.Y) &&
        float.IsFinite(rect.Width) && float.IsFinite(rect.Height);

    private static AssetPreviewImage RequiredPreview(BObject asset) =>
        EditorAssetPreview.GetAssetPreview(asset) is { } preview
            ? preview
            : throw new InvalidOperationException($"AssetPreview did not provide an image for '{asset.name}'.");
}
