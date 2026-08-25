using BEngine.Documents;
using BEngine.Editor;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal static class EditorPlayModeIsolationTests
{
    public static void Run(SceneFixture fixture)
    {
        Undo.ClearAll();
        using var harness = new EditorApplicationHarness(fixture);
        var diskBeforePlay = File.ReadAllBytes(fixture.FirstScenePath);
        var editScene = harness.ActiveScene;
        var editRoot = editScene.Find("First Root") ??
                       throw new InvalidOperationException("The Play Mode fixture root is missing.");
        var editChild = editScene.Find("First Child") ??
                        throw new InvalidOperationException("The Play Mode fixture child is missing.");
        var editSecondScene = EditorSceneManager.OpenScene(fixture.SecondScenePath, OpenSceneMode.Additive) ??
                              throw new InvalidOperationException(
                                  "The Play Mode fixture additive Scene could not be opened.");
        var editSecondRoot = editSecondScene.Find("Second Root") ??
                             throw new InvalidOperationException(
                                 "The Play Mode fixture additive root is missing.");
        TestAssert.Require(EditorSceneManager.SetActiveScene(editScene),
            "The Play Mode fixture could not restore the first Scene as active.");

        editRoot.name = "Unsaved Edit Root";
        editRoot.transform.localPosition = new Vector2(7, 11);
        editRoot.transform.localRotation = Fix64.Parse("23");
        editRoot.transform.localScale = new Vector2(3, 5);
        editChild.SetActive(false);
        var editOrderA = editScene.CreateGameObject("Order Child A");
        var editOrderB = editScene.CreateGameObject("Order Child B");
        var editAlternateParent = editScene.CreateGameObject("Alternate Parent");
        editOrderA.transform.SetParent(editRoot.transform, false);
        editOrderB.transform.SetParent(editRoot.transform, false);
        editOrderB.transform.SetSiblingIndex(0);
        editChild.transform.SetSiblingIndex(1);
        editOrderA.transform.SetSiblingIndex(2);
        var editSiblingOrder = editRoot.transform.children.Select(item => item.gameObject).ToArray();

        var editSprite = editRoot.AddComponent<SpriteRenderer>();
        editSprite.enabled = true;
        editSprite.orderInLayer = 17;
        editSprite.color = new Color(Fix64.Parse("0.2"), Fix64.Parse("0.4"),
            Fix64.Parse("0.6"), Fix64.One);
        var editMaterialColor = new Color(Fix64.Parse("0.15"), Fix64.Parse("0.35"),
            Fix64.Parse("0.55"), Fix64.Parse("0.75"));
        var editMaterialFloat = Fix64.Parse("2.5");
        var editMaterial = new Material(Shader.Find("BEngine/PlayModeMirrorTest"))
        {
            name = "Unsaved Edit Material",
            hideFlags = HideFlags.DontSaveInBuild
        };
        editMaterial.SetColor("_MirrorColor", editMaterialColor);
        editMaterial.SetFloat("_MirrorFloat", editMaterialFloat);
        editSprite.material = editMaterial;

        var editProbe = editRoot.AddComponent<PlayModeMirrorProbe>();
        editProbe.primitiveValue = 37;
        editProbe.enabled = true;
        editProbe.numbers = [2, 3, 5, 7];
        editProbe.points = [new Vector2(11, 13), new Vector2(17, 19)];
        editProbe.sameSceneObject = editChild;
        editProbe.sameSceneComponent = editSprite;
        editProbe.ownerRoot = editRoot;
        editProbe.crossSceneObject = editSecondRoot;
        editProbe.crossSceneComponent = editSecondRoot.transform;
        editProbe.objectList = [editChild, editSecondRoot];
        editProbe.sharedMaterial = editMaterial;
        var editReferenceData = new PlayModeMirrorData
        {
            label = "Unsaved reference data",
            values = [23, 29, 31],
            target = editSecondRoot,
            targetComponent = editSprite
        };
        editReferenceData.self = editReferenceData;
        editProbe.referenceData = editReferenceData;
        editProbe.referenceAlias = editReferenceData;

        editRoot.hideFlags = HideFlags.HideInInspector;
        editChild.hideFlags = HideFlags.NotEditable;
        editProbe.hideFlags = HideFlags.HideInHierarchy;
        EditorUtility.SetDirty(editRoot.transform);
        TestAssert.Require(EditorSceneManager.MarkSceneDirty(editScene),
            "The unsaved edit Scene could not be marked dirty before Play Mode.");
        harness.SelectGameObject(editChild);

        var secondDiskBeforePlay = File.ReadAllBytes(fixture.SecondScenePath);
        var editTransformDirtyCount = EditorUtility.GetDirtyCount(editRoot.transform);
        var editSpriteDirtyCount = EditorUtility.GetDirtyCount(editSprite);
        Scene? playScene = null;
        Scene? playSecondScene = null;
        GameObject? playRoot = null;
        SpriteRenderer? playSprite = null;
        PlayModeMirrorProbe? playProbe = null;
        Material? playMaterial = null;
        GameObject? runtimeOnly = null;
        try
        {
            harness.EnterPlay();
            playScene = harness.ActiveScene;
            playRoot = playScene.Find(editRoot.Id) ??
                       throw new InvalidOperationException("The Play Mode mirror lost the edited root.");
            var playChild = playScene.Find(editChild.Id) ??
                            throw new InvalidOperationException("The Play Mode mirror lost the edited child.");
            var playOrderA = playScene.Find(editOrderA.Id) ??
                             throw new InvalidOperationException("The Play Mode mirror lost its ordered child.");
            var playAlternateParent = playScene.Find(editAlternateParent.Id) ??
                                      throw new InvalidOperationException(
                                          "The Play Mode mirror lost its alternate parent.");
            playSecondScene = harness.OpenScenes.Single(scene => scene.Id == editSecondScene.Id);
            var playSecondRoot = playSecondScene.Find(editSecondRoot.Id) ??
                                 throw new InvalidOperationException(
                                     "The Play Mode additive mirror lost its edited root.");
            playSprite = playRoot.GetComponent<SpriteRenderer>() ??
                         throw new InvalidOperationException("The Play Mode mirror lost the SpriteRenderer.");
            playProbe = playRoot.GetComponent<PlayModeMirrorProbe>() ??
                        throw new InvalidOperationException("The Play Mode mirror lost the deep-copy probe.");
            playMaterial = playSprite.material;

            TestAssert.Require(!ReferenceEquals(playScene, editScene) && playScene.Id == editScene.Id &&
                               !ReferenceEquals(playSecondScene, editSecondScene) &&
                               playSecondScene.Id == editSecondScene.Id && harness.OpenScenes.Count == 2 &&
                               ReferenceEquals(harness.OpenScenes[0], playScene) &&
                               ReferenceEquals(harness.OpenScenes[1], playSecondScene),
                "Entering Play Mode did not replace both editor Scenes with same-Guid runtime mirrors.");
            TestAssert.Require(editScene.isCreated && !ReferenceEquals(playRoot, editRoot) &&
                               playRoot.Id == editRoot.Id && !ReferenceEquals(playChild, editChild) &&
                               playChild.Id == editChild.Id && !ReferenceEquals(playSprite, editSprite) &&
                               playSprite.Id == editSprite.Id,
                "The Play Mode Scene reused an edit-time GameObject or Component instance.");
            TestAssert.Require(playRoot.name == "Unsaved Edit Root" &&
                               playRoot.transform.localPosition == new Vector2(7, 11) &&
                               playRoot.transform.localRotation == Fix64.Parse("23") &&
                               playRoot.transform.localScale == new Vector2(3, 5) &&
                               !playChild.activeSelf && ReferenceEquals(playChild.transform.parent, playRoot.transform) &&
                               playSprite.enabled && playSprite.orderInLayer == 17 &&
                               playSprite.color.Equals(editSprite.color),
                "The Play Mode mirror was loaded from disk instead of cloning unsaved in-memory edit state.");
            TestAssert.Require(playRoot.hideFlags == editRoot.hideFlags &&
                               playChild.hideFlags == editChild.hideFlags &&
                               playProbe.hideFlags == editProbe.hideFlags &&
                               playRoot.transform.children.Select(item => item.gameObject.Id)
                                   .SequenceEqual(editSiblingOrder.Select(item => item.Id)),
                "The Play Mode mirror lost hide flags or edited child sibling order.");
            TestAssert.Require(!ReferenceEquals(playProbe.numbers, editProbe.numbers) &&
                               playProbe.primitiveValue == 37 && playProbe.enabled &&
                               playProbe.numbers.SequenceEqual(editProbe.numbers) &&
                               !ReferenceEquals(playProbe.points, editProbe.points) &&
                               playProbe.points.SequenceEqual(editProbe.points) &&
                               !ReferenceEquals(playProbe.referenceData, editProbe.referenceData) &&
                               ReferenceEquals(playProbe.referenceData, playProbe.referenceAlias) &&
                               ReferenceEquals(playProbe.referenceData?.self, playProbe.referenceData) &&
                               playProbe.referenceData?.label == editReferenceData.label &&
                               playProbe.referenceData.values.SequenceEqual(editReferenceData.values),
                "The Play Mode mirror did not deeply clone List, array, or SerializeReference data.");
            TestAssert.Require(ReferenceEquals(playProbe.sameSceneObject, playChild) &&
                               ReferenceEquals(playProbe.sameSceneComponent, playSprite) &&
                               ReferenceEquals(playProbe.ownerRoot, playRoot) &&
                               ReferenceEquals(playProbe.crossSceneObject, playSecondRoot) &&
                               ReferenceEquals(playProbe.crossSceneComponent, playSecondRoot.transform) &&
                               playProbe.objectList.Count == 2 &&
                               ReferenceEquals(playProbe.objectList[0], playChild) &&
                               ReferenceEquals(playProbe.objectList[1], playSecondRoot) &&
                               ReferenceEquals(playProbe.referenceData?.target, playSecondRoot) &&
                               ReferenceEquals(playProbe.referenceData?.targetComponent, playSprite) &&
                               playProbe.afterDeserializeCount == 1 &&
                               playProbe.afterDeserializeSawCrossScene &&
                               playProbe.sawCrossSceneInAwake &&
                               editProbe.beforeSerializeCount == 0 &&
                               editProbe.afterDeserializeCount == 0 &&
                               !editProbe.afterDeserializeSawCrossScene &&
                               !editProbe.sawCrossSceneInAwake,
                "The Play Mode mirror did not remap same-Scene or cross-Scene object references.");
            TestAssert.Require(!ReferenceEquals(playMaterial, editMaterial) &&
                               ReferenceEquals(playProbe.sharedMaterial, playMaterial) &&
                               playMaterial.name == editMaterial.name &&
                               playMaterial.hideFlags == editMaterial.hideFlags &&
                               playMaterial.GetColor("_MirrorColor").Equals(editMaterialColor) &&
                               playMaterial.GetFloat("_MirrorFloat") == editMaterialFloat,
                "The Play Mode mirror did not create one shared copy of the transient edit-time Material.");
            TestAssert.Require(ReferenceEquals(harness.SelectedGameObject, playChild),
                "The edit-time selection was not remapped to the corresponding Play Mode GameObject.");
            TestAssert.Require(diskBeforePlay.SequenceEqual(File.ReadAllBytes(fixture.FirstScenePath)),
                "Entering Play Mode wrote unsaved editor state to the Scene asset.");
            TestAssert.Require(secondDiskBeforePlay.SequenceEqual(File.ReadAllBytes(fixture.SecondScenePath)),
                "Entering Play Mode wrote the additive Scene state to its asset.");

            Undo.RecordObject(playRoot.transform, "Runtime transform mutation");
            playRoot.name = "Runtime Mutated Root";
            playRoot.transform.localPosition = new Vector2(101, 202);
            playRoot.transform.localRotation = Fix64.Parse("127");
            playRoot.transform.localScale = new Vector2(13, 17);
            playRoot.SetActive(false);
            playSprite.enabled = false;
            playSprite.orderInLayer = 999;
            playSprite.color = Color.black;
            playProbe.enabled = false;
            playProbe.primitiveValue = 101;
            playRoot.hideFlags = HideFlags.None;
            playChild.hideFlags = HideFlags.None;
            playProbe.hideFlags = HideFlags.None;
            playRoot.transform.GetChild(0).SetAsLastSibling();
            playOrderA.transform.SetParent(playAlternateParent.transform, false);
            playProbe.numbers.Clear();
            playProbe.numbers.AddRange([101, 103, 107]);
            playProbe.points[0] = new Vector2(103, 107);
            playProbe.referenceData!.label = "Runtime reference data";
            playProbe.referenceData.values[0] = 109;
            playSecondRoot.name = "Runtime Mutated Second Root";
            playMaterial.name = "Runtime Mutated Material";
            playMaterial.hideFlags = HideFlags.None;
            playMaterial.SetColor("_MirrorColor", Color.black);
            playMaterial.SetFloat("_MirrorFloat", 999);
            EditorUtility.SetDirty(playRoot.transform);
            EditorUtility.SetDirty(playSprite);
            runtimeOnly = playScene.CreateGameObject("Runtime Only Object");
            runtimeOnly.transform.SetParent(playRoot.transform, false);
            playProbe.sameSceneObject = runtimeOnly;
            playProbe.sameSceneComponent = runtimeOnly.transform;
            playProbe.crossSceneObject = null;
            playProbe.crossSceneComponent = null;
            playProbe.objectList = [runtimeOnly];
            playProbe.referenceData.target = runtimeOnly;
            playProbe.referenceData.targetComponent = runtimeOnly.transform;
            Undo.RegisterCreatedObjectUndo(runtimeOnly, "Runtime object creation");
            TestAssert.Require(playScene.Destroy(playChild),
                "The runtime mirror could not destroy its cloned child.");
            TestAssert.Require(EditorSceneManager.MarkSceneDirty(playScene),
                "The runtime mirror could not exercise the dirty-Scene path.");
            harness.SelectGameObject(runtimeOnly);

            TestAssert.Require(Undo.canUndo && playScene.Find("Runtime Only Object") is not null &&
                               playScene.Find(editChild.Id) is null,
                "The Play Mode fixture did not apply its create/delete and Undo-backed mutations.");
            TestAssert.Require(playRoot.transform.localRotation == Fix64.Parse("127") &&
                               playRoot.transform.localScale == new Vector2(13, 17) &&
                               ReferenceEquals(playOrderA.transform.parent, playAlternateParent.transform),
                "The Play Mode fixture did not apply its Transform or reparenting mutations.");
            TestAssert.Require(!playProbe.enabled && playProbe.primitiveValue == 101 &&
                               playProbe.numbers.SequenceEqual([101, 103, 107]) &&
                               ReferenceEquals(playProbe.sameSceneObject, runtimeOnly) &&
                               ReferenceEquals(playProbe.sameSceneComponent, runtimeOnly.transform) &&
                               playProbe.crossSceneObject is null && playProbe.crossSceneComponent is null &&
                               playProbe.objectList.SequenceEqual([runtimeOnly]) &&
                               ReferenceEquals(playProbe.referenceData.target, runtimeOnly) &&
                               ReferenceEquals(playProbe.referenceData.targetComponent, runtimeOnly.transform),
                "The Play Mode fixture did not apply its Component field and reference mutations.");
            TestAssert.Require(editRoot.name == "Unsaved Edit Root" &&
                               editRoot.transform.localPosition == new Vector2(7, 11) && editRoot.activeSelf &&
                               editRoot.transform.localRotation == Fix64.Parse("23") &&
                               editRoot.transform.localScale == new Vector2(3, 5) &&
                               editChild.scene == editScene && !editChild.activeSelf &&
                               editSprite.enabled && editSprite.orderInLayer == 17 &&
                               editScene.Find("Runtime Only Object") is null &&
                               editRoot.hideFlags == HideFlags.HideInInspector &&
                               editChild.hideFlags == HideFlags.NotEditable &&
                               editProbe.hideFlags == HideFlags.HideInHierarchy &&
                               editProbe.enabled && editProbe.primitiveValue == 37 &&
                               editRoot.transform.children.Select(item => item.gameObject)
                                   .SequenceEqual(editSiblingOrder) &&
                               ReferenceEquals(editOrderA.transform.parent, editRoot.transform) &&
                               editAlternateParent.transform.childCount == 0 &&
                               editProbe.numbers.SequenceEqual([2, 3, 5, 7]) &&
                               editProbe.points.SequenceEqual(
                                   [new Vector2(11, 13), new Vector2(17, 19)]) &&
                               editReferenceData.label == "Unsaved reference data" &&
                               editReferenceData.values.SequenceEqual([23, 29, 31]) &&
                               ReferenceEquals(editProbe.sameSceneObject, editChild) &&
                               ReferenceEquals(editProbe.sameSceneComponent, editSprite) &&
                               ReferenceEquals(editProbe.crossSceneObject, editSecondRoot) &&
                               ReferenceEquals(editProbe.crossSceneComponent, editSecondRoot.transform) &&
                               editProbe.objectList.SequenceEqual([editChild, editSecondRoot]) &&
                               ReferenceEquals(editReferenceData.target, editSecondRoot) &&
                               ReferenceEquals(editReferenceData.targetComponent, editSprite) &&
                               editSecondRoot.name == "Second Root" &&
                               editMaterial.name == "Unsaved Edit Material" &&
                               editMaterial.hideFlags == HideFlags.DontSaveInBuild &&
                               editMaterial.GetColor("_MirrorColor").Equals(editMaterialColor) &&
                               editMaterial.GetFloat("_MirrorFloat") == editMaterialFloat,
                "A Play Mode mutation leaked immediately into the retained edit-time Scene.");
            TestAssert.Require(!EditorSceneManager.SaveScene(playScene),
                "The editor allowed a Play Mode Scene mirror to be saved to its source asset.");
            TestAssert.Require(!EditorSceneManager.SaveOpenScenes(),
                "The editor allowed Save All Scenes to persist Play Mode Scene mirrors.");
            TestAssert.Require(!EditorApplication.ExecuteMenuItem("File/Save Scene"),
                "The File/Save Scene menu command was executable in Play Mode.");
            TestAssert.Require(!EditorApplication.ExecuteMenuItem("File/Save All Scenes"),
                "The File/Save All Scenes menu command was executable in Play Mode.");

            var saveShortcut = new Event(EventType.KeyDown)
            {
                keyCode = KeyCode.S,
                modifiers = EventModifiers.Control
            };
            harness.HandleGlobalKeyboard(saveShortcut);
            TestAssert.Require(saveShortcut.type == EventType.KeyDown,
                "The disabled Ctrl+S command was consumed in Play Mode.");

            var directSaveBlocked = false;
            try
            {
                Document.SaveBObject<SceneDocument>(playScene, fixture.FirstScenePath);
            }
            catch (InvalidOperationException)
            {
                directSaveBlocked = true;
            }
            TestAssert.Require(directSaveBlocked,
                "Document.SaveBObject bypassed the Play Mode Scene persistence barrier.");

            var runtimeDocument = Document.FromBObject<SceneDocument>(playScene);
            var documentSaveBlocked = false;
            try
            {
                runtimeDocument.Save(fixture.FirstScenePath);
            }
            catch (InvalidOperationException)
            {
                documentSaveBlocked = true;
            }
            TestAssert.Require(documentSaveBlocked,
                "A SceneDocument created from the runtime mirror bypassed the persistence barrier.");

            TestAssert.Require(EditorSceneManager.SetActiveScene(playSecondScene) &&
                               ReferenceEquals(harness.ActiveScene, playSecondScene),
                "The Play Mode host could not switch between runtime Scene mirrors.");
            AssertSceneAssetsUnchanged(fixture, diskBeforePlay, secondDiskBeforePlay,
                "Play Mode save attempts or active Scene switching");

            harness.ExitPlay();

            TestAssert.Require(!harness.IsPlaying && ReferenceEquals(harness.ActiveScene, editScene) &&
                               harness.OpenScenes.Count == 2 && ReferenceEquals(harness.OpenScenes[0], editScene) &&
                               ReferenceEquals(harness.OpenScenes[1], editSecondScene),
                "Stopping Play Mode did not restore the original active/open Scene references.");
            TestAssert.Require(!playScene.isCreated && !playSecondScene.isCreated,
                "Stopping Play Mode retained a discarded runtime Scene mirror.");
            TestAssert.Require(ReferenceEquals(editScene.Find(editRoot.Id), editRoot) &&
                               ReferenceEquals(editScene.Find(editChild.Id), editChild) &&
                               ReferenceEquals(editRoot.GetComponent<SpriteRenderer>(), editSprite),
                "Stopping Play Mode rebuilt the edit Scene instead of restoring its original object references.");
            TestAssert.Require(editRoot.name == "Unsaved Edit Root" && editRoot.activeSelf &&
                               editRoot.transform.localPosition == new Vector2(7, 11) &&
                               editRoot.transform.localRotation == Fix64.Parse("23") &&
                               editRoot.transform.localScale == new Vector2(3, 5) &&
                               !editChild.activeSelf && ReferenceEquals(editChild.transform.parent, editRoot.transform) &&
                               editSprite.enabled && editSprite.orderInLayer == 17 &&
                               editSprite.color.Equals(new Color(Fix64.Parse("0.2"), Fix64.Parse("0.4"),
                                   Fix64.Parse("0.6"), Fix64.One)) &&
                               editScene.Find("Runtime Only Object") is null,
                "Stopping Play Mode did not restore the complete edit-time hierarchy and Component state.");
            TestAssert.Require(editRoot.hideFlags == HideFlags.HideInInspector &&
                               editChild.hideFlags == HideFlags.NotEditable &&
                               editProbe.hideFlags == HideFlags.HideInHierarchy &&
                               editProbe.enabled && editProbe.primitiveValue == 37 &&
                               editRoot.transform.children.Select(item => item.gameObject)
                                   .SequenceEqual(editSiblingOrder) &&
                               ReferenceEquals(editOrderA.transform.parent, editRoot.transform) &&
                               editAlternateParent.transform.childCount == 0 &&
                               ReferenceEquals(editProbe.sameSceneObject, editChild) &&
                               ReferenceEquals(editProbe.sameSceneComponent, editSprite) &&
                               ReferenceEquals(editProbe.crossSceneObject, editSecondRoot) &&
                               ReferenceEquals(editProbe.crossSceneComponent, editSecondRoot.transform) &&
                               editProbe.objectList.SequenceEqual([editChild, editSecondRoot]) &&
                               ReferenceEquals(editProbe.referenceData, editReferenceData) &&
                               ReferenceEquals(editProbe.referenceAlias, editReferenceData) &&
                               ReferenceEquals(editReferenceData.self, editReferenceData) &&
                               ReferenceEquals(editReferenceData.target, editSecondRoot) &&
                               ReferenceEquals(editReferenceData.targetComponent, editSprite) &&
                               editProbe.beforeSerializeCount == 0 &&
                               editProbe.afterDeserializeCount == 0 &&
                               !editProbe.afterDeserializeSawCrossScene &&
                               editProbe.numbers.SequenceEqual([2, 3, 5, 7]) &&
                               editProbe.points.SequenceEqual(
                                   [new Vector2(11, 13), new Vector2(17, 19)]) &&
                               editReferenceData.label == "Unsaved reference data" &&
                               editReferenceData.values.SequenceEqual([23, 29, 31]),
                "Stopping Play Mode did not restore deep data, references, flags, or sibling order.");
            TestAssert.Require(ReferenceEquals(editSprite.material, editMaterial) &&
                               ReferenceEquals(editProbe.sharedMaterial, editMaterial) &&
                               editMaterial.name == "Unsaved Edit Material" &&
                               editMaterial.hideFlags == HideFlags.DontSaveInBuild &&
                               editMaterial.GetColor("_MirrorColor").Equals(editMaterialColor) &&
                               editMaterial.GetFloat("_MirrorFloat") == editMaterialFloat,
                "Stopping Play Mode did not restore the exact edit-time Material reference and values.");
            TestAssert.Require(ReferenceEquals(harness.SelectedGameObject, editChild),
                "Stopping Play Mode did not restore the edit-time GameObject selection.");
            TestAssert.Require(harness.IsSceneDirty(editScene) &&
                               EditorUtility.GetDirtyCount(editRoot.transform) == editTransformDirtyCount &&
                               EditorUtility.GetDirtyCount(editSprite) == editSpriteDirtyCount,
                "Play Mode dirty-object changes leaked into the restored edit-time Scene.");
            TestAssert.Require(!Undo.canUndo,
                "Stopping Play Mode retained Undo operations that target discarded runtime objects.");
            AssertSceneAssetsUnchanged(fixture, diskBeforePlay, secondDiskBeforePlay,
                "Stopping Play Mode");
        }
        finally
        {
            if (harness.IsPlaying) harness.ExitPlay();
            Selection.activeGameObject = null;
            Undo.ClearAll();
            EditorUtility.ClearDirty(editRoot.transform);
            EditorUtility.ClearDirty(editSprite);
            if (playRoot is not null) EditorUtility.ClearDirty(playRoot.transform);
            if (playSprite is not null) EditorUtility.ClearDirty(playSprite);
            if (runtimeOnly is not null) EditorUtility.ClearDirty(runtimeOnly);
        }
    }

    private static void AssertSceneAssetsUnchanged(
        SceneFixture fixture,
        byte[] firstBefore,
        byte[] secondBefore,
        string operation)
    {
        TestAssert.Require(firstBefore.SequenceEqual(File.ReadAllBytes(fixture.FirstScenePath)),
            $"{operation} changed the first Scene asset bytes.");
        TestAssert.Require(secondBefore.SequenceEqual(File.ReadAllBytes(fixture.SecondScenePath)),
            $"{operation} changed the second Scene asset bytes.");
    }
}

internal sealed class PlayModeMirrorProbe : MonoBehaviour, ISerializationCallbackReceiver
{
    public int primitiveValue;
    public List<int> numbers = [];
    public Vector2[] points = [];
    [SerializeReference] public PlayModeMirrorData? referenceData;
    [SerializeReference] public PlayModeMirrorData? referenceAlias;
    public GameObject? sameSceneObject;
    public Component? sameSceneComponent;
    public GameObject? ownerRoot;
    public GameObject? crossSceneObject;
    public Component? crossSceneComponent;
    public List<GameObject> objectList = [];
    public Material? sharedMaterial;
    public int beforeSerializeCount;
    public int afterDeserializeCount;
    public bool afterDeserializeSawCrossScene;
    public bool sawCrossSceneInAwake;

    public void OnBeforeSerialize() => beforeSerializeCount++;

    public void OnAfterDeserialize()
    {
        afterDeserializeCount++;
        afterDeserializeSawCrossScene = crossSceneObject is not null &&
                                        BObject.FindObjectsByType<GameObject>()
                                            .Any(item => ReferenceEquals(item, crossSceneObject));
    }

    public override void Awake()
    {
        sawCrossSceneInAwake = crossSceneObject is not null &&
                               BObject.FindObjectsByType<GameObject>()
                                   .Any(item => ReferenceEquals(item, crossSceneObject));
    }

    public override void OnDestroy()
    {
        if (ownerRoot is null) return;
        var leakedEditObject = BObject.FindObjectsByType<GameObject>()
            .FirstOrDefault(item => item.Id == ownerRoot.Id && !ReferenceEquals(item, ownerRoot));
        if (leakedEditObject is not null) leakedEditObject.name = "Runtime OnDestroy leaked into Edit Mode";
    }
}

internal sealed class PlayModeMirrorData
{
    public string label = string.Empty;
    public List<int> values = [];
    public GameObject? target;
    public Component? targetComponent;
    public PlayModeMirrorData? self;
}
