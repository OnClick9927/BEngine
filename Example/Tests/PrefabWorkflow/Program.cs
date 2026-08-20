using BEngine;
using BEngine.Documents;
using BEngine.Editor;

namespace BEngine.ExampleTests.PrefabWorkflow;

internal static class Program
{
    private static int Main()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "BEngine-PrefabWorkflow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        try
        {
            var sourceScene = new Scene("Prefab Source");
            var root = sourceScene.CreateGameObject("Robot");
            root.tag = "Player";
            root.layer = 4;
            root.isStatic = true;
            root.transform.localPosition = new Vector2(1, 2);
            root.AddComponent<Camera2D>().size = 7;
            var child = sourceScene.CreateGameObject("Sensor");
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = new Vector2(0, 2);
            child.AddComponent<SpriteRenderer>().opacity = Fix64.Parse("0.5");

            var path = Path.Combine(testDirectory, "Robot.prefab.yaml");
            Document.SaveBObject<PrefabDocument>(root, path);
            var prefab = Document.LoadBObject<PrefabDocument, PrefabAsset>(path);
            Require(File.Exists(path), "Prefab YAML was not created.");
            var yaml = File.ReadAllText(path);
            Require(yaml.Contains("BEngine.Prefab", StringComparison.Ordinal), "Prefab format marker is missing.");
            Require(prefab.objectCount == 2 && prefab.componentCount == 4, "Prefab hierarchy statistics are wrong.");

            var loaded = Document.LoadBObject<PrefabDocument, PrefabAsset>(path);
            Require(loaded.assetId == prefab.assetId, "Prefab asset ID was not preserved.");
            var destination = new Scene("Instances");
            var first = PrefabDocumentOperations.Instantiate(loaded, destination);
            var second = PrefabDocumentOperations.Instantiate(loaded, destination);
            Require(first.Id != second.Id, "Prefab instances reused a GameObject ID.");
            Require(first.transform.Id != second.transform.Id, "Prefab instances reused a Transform ID.");
            Require(first.name == "Robot" && first.tag == "Player" && first.layer == 4 && first.isStatic,
                "GameObject values did not round-trip.");
            Require(first.transform.children.Count == 1 && first.transform.children[0].gameObject.name == "Sensor",
                "Prefab child hierarchy did not round-trip.");
            Require(first.GetComponent<Camera2D>()?.size == (Fix64)7,
                "Prefab component values did not round-trip.");
            Require(PrefabUtility.IsPartOfPrefabInstance(first), "Instantiated root is not connected to its prefab.");
            Require(PrefabUtility.IsAnyPrefabInstanceRoot(first), "Prefab root was not recognized.");

            var sceneYaml = Document.FromBObject<SceneDocument>(destination).ToYaml();
            Require(sceneYaml.Contains("prefabAsset", StringComparison.OrdinalIgnoreCase) &&
                    sceneYaml.Contains("prefabSource", StringComparison.OrdinalIgnoreCase),
                "Scene YAML did not retain prefab linkage.");
            var restoredScene = (Scene)Document.FromYaml<SceneDocument>(sceneYaml).ToBObject();
            var restored = restoredScene.rootGameObjects.First(item => item.name == "Robot");
            Require(PrefabUtility.IsPartOfPrefabInstance(restored), "Scene reload lost prefab linkage.");

            PrefabUtility.UnpackPrefabInstance(restored, PrefabUnpackMode.OutermostRoot);
            Require(!PrefabUtility.IsPartOfAnyPrefab(restored), "Unpack did not remove prefab linkage.");

            var connectedPath = Path.Combine(testDirectory, "Connected.prefab.yaml");
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, connectedPath, InteractionMode.AutomatedAction,
                out var saved);
            Require(saved && PrefabUtility.IsPartOfPrefabInstance(root),
                "SaveAsPrefabAssetAndConnect did not connect the source hierarchy.");

            Console.WriteLine("PREFAB_WORKFLOW_OK|yaml,hierarchy,components,instances,scene-link,unpack,connect");
            return 0;
        }
        finally
        {
            try { Directory.Delete(testDirectory, true); } catch { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
