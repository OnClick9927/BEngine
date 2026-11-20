using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.SceneManagement;
using BEngine.Serialization;

// Real temporary Editor commands and source providers; no host services are replaced.
public static class SceneEditingWorkflow
{
    static readonly List<string> Checks = new();
    static string Output => Environment.GetEnvironmentVariable("BENGINE_SCENE_EDITING_RESULT") ?? "";
    static int Step;
    static Scene Main;
    static string SecondPath;
    static Guid MainId;
    static double Started;
    static bool EnteredPlay, EnteredEdit;
    static int SettledPlayFrames;
    [InitializeOnLoadMethod]
    public static void Bind()
    {
        if (Output.Length == 0) return;
        Started = EditorApplication.timeSinceStartup;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode) EnteredPlay = true;
            if (state == PlayModeStateChange.EnteredEditMode) EnteredEdit = true;
        };
        EditorApplication.update += Tick;
    }
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Checks.Add(name); }
    static object Host => typeof(EditorApplication).Assembly.GetType("BEngine.Editor.EditorBridge", true)
        .GetProperty("Host", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    static object Command(string name, params object[] args) => Host.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Host, args);
    static void Tick()
    {
        if (Step == 99 || EditorApplication.isCompiling) return;
        try
        {
            if (EditorApplication.timeSinceStartup - Started > 100) throw new Exception("Play Mode workflow timeout, step " + Step);
            if (Step == 0) { Step = 1; EditChecks(); EditorApplication.isPlaying = true; return; }
            if (Step == 1)
            {
                // The workbench finalizes its asynchronous transition after update
                // callbacks. Let that frame finish before requesting another one.
                if (!EnteredPlay || SettledPlayFrames++ == 0) return;
                Step = 2;
                var playing = EditorSceneManager.GetActiveScene();
                Check(playing != null && playing != Main && playing.Id == MainId && RuntimeAssetReferences.IsTransient(playing),
                    "Actual Play Mode uses a transient scene mirror with the source GUID");
                var manager = playing.GetService<IRuntimeSceneManager>();
                Check(manager != null, "Play Mode resolves its scoped scene manager");
                var loaded = manager.LoadScene("Assets/Second.scene.yaml", LoadSceneMode.Additive);
                Check(loaded.isLoaded && loaded.isCreated && loaded.Id == new Guid("d0c108fa-b51c-4ec0-8b40-5a34315c188b"),
                    "Actual runtime source loading preserves GUID and loaded state");
                Check(loaded.path == SecondPath && RuntimeAssetReferences.IsTransient(loaded),
                    "Runtime-loaded event binds the absolute source path and transient lifetime");
                Check(EditorSceneManager.GetActiveScene() == loaded && EditorSceneManager.sceneCount == 2,
                    "Runtime-loaded scene appears in the actual workbench without duplication");
                var runtimeObject = loaded.CreateGameObject("runtime object");
                Check(RuntimeAssetReferences.IsTransient(runtimeObject) && loaded.GetService<ISceneRuntimeFactory>() != null,
                    "Late runtime objects and scene services retain their lifetime and scope");
                Check(manager.UnloadScene(loaded) && !loaded.isCreated && EditorSceneManager.sceneCount == 1,
                    "Runtime unload removes the workbench scene and disposes its content");
                EditorApplication.isPlaying = false;
                return;
            }
            if (Step == 2)
            {
                if (!EnteredEdit || EditorApplication.isPlayingOrWillChangePlaymode) return;
                Check(EditorSceneManager.GetActiveScene() == Main && Main.Id == MainId && Main.isLoaded && Main.isCreated,
                    "Leaving Play Mode restores the original edit scene and identity");
                Check(!RuntimeAssetReferences.IsTransient(Main) && EditorSceneManager.sceneCount == 1,
                    "Runtime transient marking does not contaminate the source scene");
                Complete(true, "");
            }
        }
        catch (Exception error) { Complete(false, error.ToString()); }
    }
    static void EditChecks()
    {
        Main = EditorSceneManager.GetActiveScene(); MainId = Main.Id;
        Check(MainId == new Guid("173f1a97-857b-4fa6-bc08-1a44edc35485") && Main.path.EndsWith("Main.scene.yaml"),
            "Startup scene preserves source GUID and path");
        SecondPath = Path.Combine(EditorApplication.projectPath, "Assets", "Second.scene.yaml");
        var placeholder = EditorSceneManager.OpenScene(SecondPath, OpenSceneMode.AdditiveWithoutLoading);
        Check(placeholder != null && !placeholder.isLoaded && placeholder.isCreated && placeholder.gameObjects.Count == 0,
            "AdditiveWithoutLoading creates an unloaded empty reference");
        Check(placeholder.path == SecondPath && EditorSceneManager.sceneCount == 2 && EditorSceneManager.GetActiveScene() == Main,
            "The unloaded reference retains its path without changing the active scene");
        var second = EditorSceneManager.OpenScene(SecondPath, OpenSceneMode.Additive);
        Check(!placeholder.isCreated && second != placeholder && second.Id == new Guid("d0c108fa-b51c-4ec0-8b40-5a34315c188b"),
            "Opening a reference closes the placeholder and hydrates the persistent identity");
        Check(EditorSceneManager.CloseScene(second, false) && !second.isCreated && !second.isLoaded,
            "Close while retaining a reference unloads and disposes the actual scene");
        second = EditorSceneManager.OpenScene(SecondPath, OpenSceneMode.Additive);
        Check(second.isLoaded && second.path == SecondPath && EditorSceneManager.sceneCount == 2,
            "A retained reference reopens with no duplicate entry");
        Check(EditorSceneManager.CloseScene(second) && EditorSceneManager.sceneCount == 1, "Reference removal leaves the original scene open");

        var child = Main.CreateGameObject("child"); child.transform.localPosition = new Vector2(3, 4);
        Undo.ClearAll(); Command("CreateEmptyParent", child);
        var parent = child.transform.parent.gameObject; var parentId = parent.GetInstanceID();
        Check(parent.scene == Main && Undo.canUndo, "The actual empty-parent command creates scene membership and history");
        Undo.PerformUndo(); Check(parent.scene == null && parent.isDestroyed && child.transform.parent == null,
            "Parent Undo destroys only the generated parent");
        Undo.PerformRedo(); Check(parent.scene == Main && !parent.isDestroyed && child.transform.parent.gameObject == parent && parent.GetInstanceID() == parentId,
            "Parent Redo reattaches the original parent identity");
        for (var i = 0; i < 3; i++) { Undo.PerformUndo(); Undo.PerformRedo(); }
        Check(Main.gameObjects.Count(o => o == parent) == 1, "Repeated actual parent Undo/Redo introduces no duplicate objects");
        Undo.ClearAll();

        var detached = new GameObject("detached source"); var nested = new GameObject("nested source"); nested.transform.SetParent(detached.transform, false);
        Command("DuplicateSelected", detached);
        var copy = Selection.activeGameObject;
        Check(copy != detached && copy.scene == Main && copy.transform.childCount == 1 && copy.transform.children[0].gameObject.scene == Main,
            "Actual duplicate fallback attaches the complete detached source hierarchy");
        var copiedChild = copy.transform.children[0].gameObject;
        Undo.PerformUndo(); Check(copy.scene == null && copiedChild.scene == null, "Duplicate Undo detaches both cloned nodes");
        Undo.PerformRedo(); Check(copy.scene == Main && copiedChild.scene == Main, "Duplicate Redo restores both cloned node identities");
        Undo.ClearAll();
        EditorSceneManager.MarkSceneDirty(Main);
        Check(EditorSceneManager.SaveScene(Main), "The actual scene save command uses the source codec successfully");
        var sceneCodec = RuntimeServiceContributions.GetDefault<ISceneDocumentCodec>();
        using (var saved = sceneCodec.Load(Main.path, null))
            Check(saved.Id == MainId && saved.gameObjects.Count == Main.gameObjects.Count && saved.Find(copiedChild.Id) != null,
                "Saved scene roundtrip retains GUID, hierarchy and nested object identity");

        var prefabPath = Path.Combine(EditorApplication.projectPath, "Assets", "Nested.prefab.yaml");
        var prefabCodec = RuntimeServiceContributions.GetDefault<IPrefabDocumentCodec>();
        prefabCodec.Save(parent, prefabPath, null);
        AssetDatabase.ImportAsset("Assets/Nested.prefab.yaml");
        // First import establishes the persistent asset GUID in its metadata.
        // Align this newly created source document before testing preservation.
        var prefabId = Guid.Parse(AssetDatabase.AssetPathToGUID("Assets/Nested.prefab.yaml"));
        prefabCodec.Save(parent, prefabPath, prefabId);
        AssetDatabase.ImportAsset("Assets/Nested.prefab.yaml");
        Check(prefabCodec.ReadAssetId(prefabPath) == prefabId, "The imported test prefab begins with matching source and metadata GUIDs");
        var stage = PrefabStageUtility.OpenPrefab("Assets/Nested.prefab.yaml");
        Check(stage != null && stage.scene.gameObjects.Count == 2 && stage.prefabContentsRoot.transform.childCount == 1,
            "Actual Prefab Stage attaches all nested content to its scene");
        var stagedChild = stage.prefabContentsRoot.transform.children[0].gameObject; var childId = stagedChild.Id;
        Check(stagedChild.scene == stage.scene, "Prefab Stage descendants share scene membership");
        stagedChild.name = "saved child"; stage.SavePrefab();
        var savedPrefab = prefabCodec.Load(prefabPath);
        Check(savedPrefab.Id == prefabId && prefabCodec.ReadAssetId(prefabPath) == prefabId,
            "Actual stage save preserves the prefab asset GUID");
        var restored = PrefabAssetOperations.LoadContents(savedPrefab);
        Check(restored.transform.children[0].gameObject.Id == childId && restored.transform.children[0].gameObject.name == "saved child",
            "Stage save preserves descendant identity and edits through the source provider");
        var stageScene = stage.scene; stage.CloseStage();
        Check(!stageScene.isCreated && EditorSceneManager.GetActiveScene() == Main && Main.isCreated,
            "Closing the stage releases its scene and restores the edit scene");
        // Keep the Play Mode source minimal; the edit-command results were roundtripped above.
        foreach (var item in Main.GetRootGameObjects()) Main.Destroy(item);
        EditorSceneManager.MarkSceneDirty(Main); EditorSceneManager.SaveScene(Main); Undo.ClearAll();
    }
    static void Complete(bool passed, string error)
    {
        Step = 99;
        File.WriteAllText(Path.Combine(Output, "Result.json"), JsonSerializer.Serialize(new { Passed = passed, Checks = Checks.Count, Details = Checks, Error = error }));
        EditorApplication.Exit(passed ? 0 : 1);
    }
}
