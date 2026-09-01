using BEngine.Editor;
using BEngine.Serialization;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal static class EditorPlayModeValueCloneTests
{
    internal static void Run(SceneFixture fixture)
    {
        using var harness = new EditorApplicationHarness(fixture);
        var editScene = harness.ActiveScene;
        var editRoot = editScene.Find("First Root") ??
                       throw new InvalidOperationException("The value-clone fixture root is missing.");
        var editChild = editScene.Find("First Child") ??
                        throw new InvalidOperationException("The value-clone fixture child is missing.");
        var editShader = Shader.Find("BEngine/PlayModeValueClone");
        editShader.name = "Edit Value Clone Shader";
        editShader.hideFlags = HideFlags.HideInInspector;
        var editTextAsset = new BEngine.TextAsset(
            "Play Mode value clone text", "Assets/PlayModeValueClone.txt")
        {
            name = "Edit Value Clone Text",
            hideFlags = HideFlags.DontSaveInBuild
        };
        var editPrefab = PrefabAssetSerialization.Deserialize(
            PrefabAssetSerialization.Serialize(editRoot),
            "Assets/PlayModeValueClone.prefab.yaml");
        editPrefab.name = "Edit Value Clone Prefab";
        editPrefab.hideFlags = HideFlags.NotEditable;
        var editMaterial = new Material(editShader)
        {
            name = "Edit Value Clone Material"
        };
        var editNumbers = new List<int> { 3, 5, 8 };
        var delegateTarget = new PlayModeDelegateTarget(editRoot);
        var editProbe = editRoot.AddComponent<PlayModeValueCloneProbe>();
        editProbe.payload = new PlayModeStructPayload
        {
            values = editNumbers,
            target = editChild,
            material = editMaterial,
            callback = delegateTarget.MutateEditObject,
            nested = new PlayModeReadonlyStructPayload(
                editNumbers, editChild, editMaterial, delegateTarget.MutateEditObject)
        };
        editProbe.callback = delegateTarget.MutateEditObject;
        editProbe.callbackProperty = delegateTarget.MutateEditObject;
        editProbe.callbacks = [delegateTarget.MutateEditObject];
        editProbe.boxedCallback = (Action)delegateTarget.MutateEditObject;
        editProbe.shaderAsset = editShader;
        editProbe.textAsset = editTextAsset;
        editProbe.prefabAsset = editPrefab;
        editProbe.assetContainer = [editShader, editTextAsset, editPrefab, editShader];
        var editSelectedAsset = ScriptableObject.CreateInstance<PlayModeStandaloneSelectionAsset>();
        editSelectedAsset.name = "Edit Standalone Selection";
        editSelectedAsset.value = 41;
        Selection.activeObject = editSelectedAsset;
        harness.LockInspector(editSelectedAsset);

        var serializedMembers = ComponentFieldSerializer.GetSerializableMembers(
            typeof(PlayModeValueCloneProbe));
        TestAssert.Require(serializedMembers.All(member =>
                !typeof(Delegate).IsAssignableFrom(ComponentFieldSerializer.GetMemberType(member))),
            "Delegate fields or properties remained in the Component serialization contract.");

        try
        {
            harness.EnterPlay();
            var playRoot = harness.ActiveScene.Find(editRoot.Id) ??
                           throw new InvalidOperationException("The value-clone Play mirror lost its root.");
            var playChild = harness.ActiveScene.Find(editChild.Id) ??
                            throw new InvalidOperationException("The value-clone Play mirror lost its child.");
            var playProbe = playRoot.GetComponent<PlayModeValueCloneProbe>() ??
                            throw new InvalidOperationException("The value-clone Play mirror lost its probe.");
            var playPayload = playProbe.payload;
            var playShader = playProbe.shaderAsset ??
                             throw new InvalidOperationException("The value-clone Play mirror lost its Shader.");
            var playTextAsset = playProbe.textAsset ??
                                throw new InvalidOperationException("The value-clone Play mirror lost its TextAsset.");
            var playPrefab = playProbe.prefabAsset ??
                             throw new InvalidOperationException("The value-clone Play mirror lost its PrefabAsset.");
            var selectedPlayAsset = harness.SelectedAsset as PlayModeStandaloneSelectionAsset ??
                                    throw new InvalidOperationException(
                                        "The standalone selected asset was not mapped into Play Mode.");

            TestAssert.Require(!ReferenceEquals(playPayload.values, editNumbers) &&
                               playPayload.values.SequenceEqual(editNumbers) &&
                               ReferenceEquals(playPayload.values, playPayload.nested.values) &&
                               ReferenceEquals(playPayload.target, playChild) &&
                               !ReferenceEquals(playPayload.material, editMaterial) &&
                               ReferenceEquals(playPayload.material, playPayload.nested.material) &&
                               !ReferenceEquals(selectedPlayAsset, editSelectedAsset) &&
                               selectedPlayAsset.value == editSelectedAsset.value &&
                               ReferenceEquals(harness.InspectorTarget, selectedPlayAsset) &&
                               ReferenceEquals(playPayload.nested.target, playChild),
                "A struct or selected/locked asset reference was not deeply cloned and remapped.");
            TestAssert.Require(playPayload.callback is null && playPayload.nested.callback is null &&
                               playProbe.callback is null && playProbe.callbackProperty is null &&
                               playProbe.callbacks.Count == 1 && playProbe.callbacks[0] is null &&
                               playProbe.boxedCallback is null,
                "A Delegate carrying an edit-time target entered the Play Mode object graph.");
            TestAssert.Require(!ReferenceEquals(playShader, editShader) &&
                               !ReferenceEquals(playTextAsset, editTextAsset) &&
                               !ReferenceEquals(playPrefab, editPrefab) &&
                               playProbe.assetContainer.Count == 4 &&
                               ReferenceEquals(playProbe.assetContainer[0], playShader) &&
                               ReferenceEquals(playProbe.assetContainer[1], playTextAsset) &&
                               ReferenceEquals(playProbe.assetContainer[2], playPrefab) &&
                               ReferenceEquals(playProbe.assetContainer[3], playShader) &&
                               ReferenceEquals(playPayload.material!.shader, playShader) &&
                               playTextAsset.text == editTextAsset.text &&
                               playTextAsset.path == editTextAsset.path &&
                               playPrefab.assetId == editPrefab.assetId &&
                               playPrefab.assetPath == editPrefab.assetPath,
                "Shader, TextAsset, or PrefabAsset references were shared or lost their source aliases.");

            var forbiddenAssetPath = Path.Combine(fixture.Workspace.AssetsPath, "RuntimeShader.yaml");
            var runtimeAssetSaveBlocked = false;
            try
            {
                YamlUtility.Save(playShader, forbiddenAssetPath);
            }
            catch (InvalidOperationException)
            {
                runtimeAssetSaveBlocked = true;
            }
            TestAssert.Require(runtimeAssetSaveBlocked && !File.Exists(forbiddenAssetPath),
                "A runtime-only asset bypassed the persistence barrier through YamlUtility.Save.");

            playPayload.callback?.Invoke();
            playPayload.nested.callback?.Invoke();
            playProbe.callback?.Invoke();
            playProbe.callbackProperty?.Invoke();
            playProbe.callbacks[0]?.Invoke();
            (playProbe.boxedCallback as Action)?.Invoke();
            playPayload.values[0] = 89;
            playPayload.target!.name = "Runtime Struct Target";
            playPayload.material!.name = "Runtime Struct Material";
            selectedPlayAsset.name = "Runtime Standalone Selection";
            selectedPlayAsset.value = 99;
            playShader.name = "Runtime Value Clone Shader";
            playShader.hideFlags = HideFlags.None;
            playTextAsset.name = "Runtime Value Clone Text";
            playTextAsset.hideFlags = HideFlags.None;
            playPrefab.name = "Runtime Value Clone Prefab";
            playPrefab.hideFlags = HideFlags.None;

            TestAssert.Require(delegateTarget.InvocationCount == 0 && editRoot.name == "First Root" &&
                               editChild.name == "First Child" && editNumbers.SequenceEqual([3, 5, 8]) &&
                               editMaterial.name == "Edit Value Clone Material" &&
                               editSelectedAsset.name == "Edit Standalone Selection" &&
                               editSelectedAsset.value == 41 &&
                               editShader.name == "Edit Value Clone Shader" &&
                               editShader.hideFlags == HideFlags.HideInInspector &&
                               editTextAsset.name == "Edit Value Clone Text" &&
                               editTextAsset.hideFlags == HideFlags.DontSaveInBuild &&
                               editPrefab.name == "Edit Value Clone Prefab" &&
                               editPrefab.hideFlags == HideFlags.NotEditable,
                "A nested struct or Delegate mutation leaked into the retained edit-time graph.");

            harness.ExitPlay();
            TestAssert.Require(editProbe.payload.values.SequenceEqual([3, 5, 8]) &&
                               ReferenceEquals(editProbe.payload.values, editNumbers) &&
                               ReferenceEquals(editProbe.payload.target, editChild) &&
                               ReferenceEquals(editProbe.payload.material, editMaterial) &&
                               ReferenceEquals(editProbe.payload.nested.values, editNumbers) &&
                               editProbe.callback is not null && editProbe.callbackProperty is not null &&
                               editProbe.callbacks.Count == 1 && editProbe.callbacks[0] is not null &&
                               editProbe.boxedCallback is Action &&
                               ReferenceEquals(editProbe.shaderAsset, editShader) &&
                               ReferenceEquals(editProbe.textAsset, editTextAsset) &&
                               ReferenceEquals(editProbe.prefabAsset, editPrefab) &&
                               ReferenceEquals(harness.SelectedAsset, editSelectedAsset) &&
                               ReferenceEquals(Selection.activeObject, editSelectedAsset) &&
                               ReferenceEquals(harness.InspectorTarget, editSelectedAsset) &&
                               editSelectedAsset.name == "Edit Standalone Selection" &&
                               editSelectedAsset.value == 41 &&
                               editProbe.assetContainer.SequenceEqual(
                                   new BAsset[] { editShader, editTextAsset, editPrefab, editShader }) &&
                               editShader.name == "Edit Value Clone Shader" &&
                               editShader.hideFlags == HideFlags.HideInInspector &&
                               editTextAsset.name == "Edit Value Clone Text" &&
                               editTextAsset.hideFlags == HideFlags.DontSaveInBuild &&
                               editPrefab.name == "Edit Value Clone Prefab" &&
                               editPrefab.hideFlags == HideFlags.NotEditable,
                "Stopping Play Mode did not preserve the original struct and Delegate state.");
        }
        finally
        {
            if (harness.IsPlaying) harness.ExitPlay();
        }
    }
}

internal sealed class PlayModeValueCloneProbe : MonoBehaviour
{
    public PlayModeStructPayload payload;
    public Action? callback;
    public Action? callbackProperty { get; set; }
    public List<Action?> callbacks = [];
    public object? boxedCallback;
    public Shader? shaderAsset;
    public BEngine.TextAsset? textAsset;
    public PrefabAsset? prefabAsset;
    public List<BAsset> assetContainer = [];
}

internal struct PlayModeStructPayload
{
    public List<int> values;
    public GameObject? target;
    public Material? material;
    public Action? callback;
    public PlayModeReadonlyStructPayload nested;
}

internal readonly struct PlayModeReadonlyStructPayload(
    List<int> values,
    GameObject target,
    Material material,
    Action callback)
{
    public readonly List<int> values = values;
    public readonly GameObject target = target;
    public readonly Material material = material;
    public readonly Action callback = callback;
}

internal sealed class PlayModeDelegateTarget(GameObject editObject)
{
    internal int InvocationCount { get; private set; }

    internal void MutateEditObject()
    {
        InvocationCount++;
        editObject.name = "Delegate Mutated Edit Object";
    }
}

internal sealed class PlayModeStandaloneSelectionAsset : ScriptableObject
{
    public int value;
}
