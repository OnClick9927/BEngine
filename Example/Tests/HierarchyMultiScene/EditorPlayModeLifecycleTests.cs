using System.Reflection;
using BEngine.Editor;
using BEngine.SceneManagement;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal static class EditorPlayModeLifecycleTests
{
    public static void Run(SceneFixture fixture)
    {
        VerifyOpenGameWindowReceivesPlayFocus(fixture);
        VerifyTransitionReentrancyTeardownAndInspectorMapping(fixture);
        VerifyRuntimeGlobalStateRestoration(fixture);
        VerifySingleSceneSwitchRestoresEditSession(fixture);
        VerifyDynamicRuntimeSceneLifecycle(fixture);
    }

    private static void VerifyOpenGameWindowReceivesPlayFocus(SceneFixture fixture)
    {
        using var harness = new EditorApplicationHarness(fixture);
        try
        {
            harness.SelectDockedWindow(harness.SceneWindow);
            TestAssert.Require(harness.IsWindowSelected(harness.SceneWindow) &&
                               ReferenceEquals(EditorWindow.focusedWindow, harness.SceneWindow),
                "The Play focus fixture could not select the Scene window.");

            harness.EnterPlay();
            TestAssert.Require(harness.IsWindowSelected(harness.GameWindow) &&
                               ReferenceEquals(EditorWindow.focusedWindow, harness.GameWindow),
                "Entering Play Mode did not select and focus the open Game window.");
            harness.ExitPlay();

            harness.FloatWindow(harness.GameWindow);
            harness.SelectDockedWindow(harness.SceneWindow);
            TestAssert.Require(!harness.IsWindowSelected(harness.GameWindow),
                "The Play focus fixture could not float the Game window.");
            harness.EnterPlay();
            TestAssert.Require(ReferenceEquals(EditorWindow.focusedWindow, harness.GameWindow),
                "Entering Play Mode did not focus the open floating Game window.");
            harness.ExitPlay();

            harness.CloseWindow(harness.GameWindow);
            harness.SelectDockedWindow(harness.SceneWindow);
            harness.EnterPlay();
            TestAssert.Require(!harness.IsWindowOpen(harness.GameWindow) &&
                               ReferenceEquals(EditorWindow.focusedWindow, harness.SceneWindow),
                "Entering Play Mode reopened or focused a closed Game window.");
        }
        finally
        {
            if (harness.IsPlaying) harness.ExitPlay();
        }
    }

    private static void VerifyTransitionReentrancyTeardownAndInspectorMapping(SceneFixture fixture)
    {
        using var harness = new EditorApplicationHarness(fixture);
        using var callbacks = harness.AttachRuntimeSceneCallbacks();
        var editScene = harness.ActiveScene;
        var editRoot = editScene.Find("First Root") ??
                       throw new InvalidOperationException("The lifecycle fixture lost its root.");
        var editChild = editScene.Find("First Child") ??
                        throw new InvalidOperationException("The lifecycle fixture lost its child.");
        var editSecondScene = EditorSceneManager.OpenScene(fixture.SecondScenePath, OpenSceneMode.Additive) ??
                              throw new InvalidOperationException("The lifecycle fixture could not open its additive Scene.");
        EditorSceneManager.SetActiveScene(editScene);
        editRoot.AddComponent<PlayModeTeardownSaveProbe>();
        harness.SelectGameObject(editChild);
        harness.LockInspector(editRoot);
        var diskBeforePlay = File.ReadAllBytes(fixture.FirstScenePath);
        var observedStates = new List<PlayModeStateChange>();
        var saveDuringEnter = true;
        var switchedActiveSceneDuringEnter = false;

        PlayModeTeardownSaveProbe.ResetState();
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        try
        {
            harness.EnterPlay();
            var runtimeScene = harness.OpenScenes.Single(scene => scene.Id == editScene.Id);
            var runtimeSecondScene = harness.OpenScenes.Single(scene => scene.Id == editSecondScene.Id);
            var runtimeRoot = runtimeScene.Find(editRoot.Id) ??
                              throw new InvalidOperationException("The runtime mirror lost its root.");
            var runtimeChild = runtimeScene.Find(editChild.Id) ??
                               throw new InvalidOperationException("The runtime mirror lost its child.");
            TestAssert.Require(harness.IsPlaying && switchedActiveSceneDuringEnter &&
                               ReferenceEquals(harness.ActiveScene, runtimeSecondScene) &&
                               ReferenceEquals(harness.SelectedGameObject, runtimeChild) &&
                               ReferenceEquals(harness.InspectorTarget, runtimeRoot),
                "Entering Play Mode did not synchronize an event-driven active Scene switch or remap editor targets.");

            runtimeRoot.name = "Runtime teardown mutation";
            harness.ExitPlay();

            TestAssert.Require(!harness.IsPlaying && ReferenceEquals(harness.ActiveScene, editScene) &&
                               ReferenceEquals(harness.SelectedGameObject, editChild) &&
                               ReferenceEquals(harness.InspectorTarget, editRoot),
                "Stopping Play Mode did not restore selection and the exact locked Inspector target.");
            TestAssert.Require(!saveDuringEnter && PlayModeTeardownSaveProbe.DestroyCount == 1 &&
                               PlayModeTeardownSaveProbe.WasPlayingDuringDestroy &&
                               !PlayModeTeardownSaveProbe.SaveResult,
                "A Play transition or runtime OnDestroy was allowed to save a Scene mirror.");
            TestAssert.Require(editRoot.name == "First Root" &&
                               diskBeforePlay.SequenceEqual(File.ReadAllBytes(fixture.FirstScenePath)),
                "Runtime teardown changed the retained edit Scene or its asset.");
            TestAssert.Require(observedStates.SequenceEqual(
                    [PlayModeStateChange.ExitingEditMode, PlayModeStateChange.EnteredPlayMode,
                     PlayModeStateChange.ExitingPlayMode, PlayModeStateChange.EnteredEditMode]),
                "A reentrant Play state request duplicated or skipped transition events.");
        }
        finally
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            PlayModeTeardownSaveProbe.Enabled = false;
            if (harness.IsPlaying) harness.ExitPlay();
        }

        void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            observedStates.Add(state);
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    saveDuringEnter = EditorSceneManager.SaveScene(harness.ActiveScene);
                    EditorApplication.isPlaying = true;
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    var runtimeSecondScene = harness.OpenScenes.Single(scene => scene.Id == editSecondScene.Id);
                    switchedActiveSceneDuringEnter =
                        harness.RuntimeSceneManager.SetActiveScene(runtimeSecondScene) &&
                        ReferenceEquals(harness.ActiveScene, runtimeSecondScene);
                    EditorApplication.isPlaying = false;
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    EditorApplication.isPlaying = false;
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    EditorApplication.isPlaying = true;
                    break;
            }
        }
    }

    private static void VerifyRuntimeGlobalStateRestoration(SceneFixture fixture)
    {
        var processBaseline = RuntimeGlobalState.Capture();
        try
        {
            using var harness = new EditorApplicationHarness(fixture);
            var editBaseline = RuntimeGlobalState.Capture();
            try
            {
                harness.EnterPlay();

                SetTimeRuntimeValues(
                    DifferentFrom(editBaseline.DeltaTime, "0.041"),
                    DifferentFrom(editBaseline.UnscaledDeltaTime, "0.043"),
                    DifferentFrom(editBaseline.TimeValue, "11"),
                    DifferentFrom(editBaseline.UnscaledTime, "13"),
                    DifferentFrom(editBaseline.FixedTime, "17"),
                    !editBaseline.InFixedTimeStep,
                    editBaseline.FrameCount ^ 0x5A5A_5A5A_5A5A_5A5A);
                Time.fixedDeltaTime = DifferentFrom(editBaseline.FixedDeltaTime, "0.031");
                Time.timeScale = DifferentFrom(editBaseline.TimeScale, "0.25");
                Time.maximumDeltaTime = DifferentFrom(editBaseline.MaximumDeltaTime, "0.75");
                Application.targetFrameRate = editBaseline.TargetFrameRate == 144 ? 73 : 144;
                QualitySettings.SetQualityLevel((editBaseline.QualityLevel + 1) % QualitySettings.names.Length);
                QualitySettings.vSyncCount = editBaseline.VSyncCount == 0 ? 2 : 0;
                QualitySettings.antiAliasing = editBaseline.AntiAliasing == 8 ? 4 : 8;
                QualitySettings.realtimeReflectionProbes = !editBaseline.RealtimeReflectionProbes;
                QualitySettings.shadowDistance = DifferentFrom(editBaseline.ShadowDistance, "123");

                var editResolution = editBaseline.Resolution;
                var runtimeFullScreenMode = editBaseline.FullScreenMode == FullScreenMode.Windowed
                    ? FullScreenMode.FullScreenWindow
                    : FullScreenMode.Windowed;
                Screen.SetResolution(editResolution.width + 113, editResolution.height + 127,
                    runtimeFullScreenMode, editResolution.refreshRate + 3);

                Cursor.visible = !editBaseline.CursorVisible;
                Cursor.lockState = editBaseline.CursorLockState == CursorLockMode.Locked
                    ? CursorLockMode.Confined
                    : CursorLockMode.Locked;
                var runtimeCursorTexture = new BEngine.TextAsset("runtime cursor", "RuntimeCursor.txt");
                var runtimeHotspot = editBaseline.CursorHotspot == new Vector2(3, 5)
                    ? new Vector2(7, 11)
                    : new Vector2(3, 5);
                var runtimeCursorMode = editBaseline.CursorMode == CursorMode.Auto
                    ? CursorMode.ForceSoftware
                    : CursorMode.Auto;
                Cursor.SetCursor(runtimeCursorTexture, runtimeHotspot, runtimeCursorMode);

                var runtimeRandomValue = editBaseline.RandomState.Value ^ 0xA5A5_A5A5_A5A5_A5A5UL;
                if (runtimeRandomValue == 0 || runtimeRandomValue == editBaseline.RandomState.Value)
                    runtimeRandomValue = 0x1357_9BDF_2468_ACE1UL;
                BEngine.Random.state = new BEngine.Random.State(runtimeRandomValue);

                RuntimeGlobalState.Capture().RequireDifferentFrom(editBaseline);
                harness.ExitPlay();
                editBaseline.RequireCurrent("Stopping Play Mode");
            }
            finally
            {
                if (harness.IsPlaying) harness.ExitPlay();
            }
        }
        finally
        {
            processBaseline.Restore();
        }
    }

    private static void VerifySingleSceneSwitchRestoresEditSession(SceneFixture fixture)
    {
        using var harness = new EditorApplicationHarness(fixture);
        var editFirstScene = harness.ActiveScene;
        var editFirstRoot = editFirstScene.Find("First Root") ??
                            throw new InvalidOperationException("The Single-switch fixture lost its first root.");
        var editSecondScene = EditorSceneManager.OpenScene(fixture.SecondScenePath, OpenSceneMode.Additive) ??
                              throw new InvalidOperationException(
                                  "The Single-switch fixture could not open its additive Scene.");
        var editSecondRoot = editSecondScene.Find("Second Root") ??
                             throw new InvalidOperationException("The Single-switch fixture lost its second root.");
        TestAssert.Require(EditorSceneManager.SetActiveScene(editFirstScene),
            "The Single-switch fixture could not restore its first active Scene.");

        var firstBytesBeforePlay = File.ReadAllBytes(fixture.FirstScenePath);
        var secondBytesBeforePlay = File.ReadAllBytes(fixture.SecondScenePath);
        Scene? runtimeFirstScene = null;
        Scene? runtimeSecondScene = null;
        try
        {
            harness.EnterPlay();
            runtimeFirstScene = harness.OpenScenes.Single(scene => scene.Id == editFirstScene.Id);
            runtimeSecondScene = harness.OpenScenes.Single(scene => scene.Id == editSecondScene.Id);
            var runtimeFirstRoot = runtimeFirstScene.Find(editFirstRoot.Id) ??
                                   throw new InvalidOperationException(
                                       "The Single-switch runtime mirror lost its first root.");
            var runtimeSecondRoot = runtimeSecondScene.Find(editSecondRoot.Id) ??
                                    throw new InvalidOperationException(
                                        "The Single-switch runtime mirror lost its second root.");

            runtimeFirstRoot.name = "Runtime Single-Switch Root";
            runtimeFirstRoot.transform.localPosition = new Vector2(211, 223);
            runtimeFirstRoot.transform.localRotation = Fix64.Parse("127");
            runtimeFirstRoot.transform.localScale = new Vector2(17, 19);
            var runtimeOnly = runtimeFirstScene.CreateGameObject("Runtime Single-Switch Object");
            runtimeOnly.transform.SetParent(runtimeFirstRoot.transform, false);
            var runtimeProbe = runtimeSecondRoot.AddComponent<PlayModeMirrorProbe>();
            runtimeProbe.enabled = false;
            runtimeProbe.primitiveValue = 227;
            runtimeProbe.numbers = [229, 233];
            runtimeProbe.sameSceneObject = runtimeOnly;
            runtimeProbe.crossSceneObject = runtimeSecondRoot;
            runtimeSecondRoot.name = "Runtime Single-Switch Second Root";
            TestAssert.Require(EditorSceneManager.MarkSceneDirty(runtimeFirstScene) &&
                               EditorSceneManager.MarkSceneDirty(runtimeSecondScene),
                "The Single-switch fixture could not mark both runtime mirrors dirty.");
            AssertSceneAssetsUnchanged(fixture, firstBytesBeforePlay, secondBytesBeforePlay,
                "Runtime mutation before Single Scene switching");

            var switched = EditorSceneManager.OpenScene(fixture.SecondScenePath, OpenSceneMode.Single);

            TestAssert.Require(!harness.IsPlaying && ReferenceEquals(switched, editSecondScene) &&
                               ReferenceEquals(harness.ActiveScene, editSecondScene) &&
                               harness.OpenScenes.Count == 1 &&
                               ReferenceEquals(harness.OpenScenes[0], editSecondScene) && editSecondScene.isCreated &&
                               !editFirstScene.isCreated && !runtimeFirstScene.isCreated &&
                               !runtimeSecondScene.isCreated,
                "Single Scene switching from Play Mode retained or returned a stale runtime Scene entry.");
            TestAssert.Require(editSecondRoot.name == "Second Root" &&
                               editSecondRoot.GetComponent<PlayModeMirrorProbe>() is null &&
                               editSecondScene.Find("Runtime Single-Switch Object") is null,
                "Single Scene switching leaked runtime hierarchy or Component state into the editor Scene.");
            AssertSceneAssetsUnchanged(fixture, firstBytesBeforePlay, secondBytesBeforePlay,
                "Single Scene switching from Play Mode");
        }
        finally
        {
            if (harness.IsPlaying) harness.ExitPlay();
        }
    }

    private static void VerifyDynamicRuntimeSceneLifecycle(SceneFixture fixture)
    {
        using var harness = new EditorApplicationHarness(fixture);
        using var callbacks = harness.AttachRuntimeSceneCallbacks();
        harness.EnterPlay();
        var firstRuntimeScene = harness.ActiveScene;
        var manager = harness.RuntimeSceneManager;
        try
        {
            var additiveScene = manager.LoadScene(fixture.SecondScenePath, LoadSceneMode.Additive);
            TestAssert.Require(ReferenceEquals(harness.ActiveScene, additiveScene) && harness.RuntimeCount == 2,
                "A dynamically loaded runtime Scene did not become the editor host's active Scene.");
            TestAssert.Require(manager.UnloadScene(additiveScene) && !additiveScene.isCreated &&
                               ReferenceEquals(harness.ActiveScene, firstRuntimeScene) &&
                               harness.RuntimeCount == 1,
                "Unloading the active runtime Scene left the editor host on a disposed Scene.");

            const string failingRegistration = "editor-play-dynamic-start-failure";
            RuntimeSystemRegistry.Register(failingRegistration, static () =>
                throw new InvalidOperationException("Expected dynamic runtime start failure."));
            Scene? failedScene = null;
            try
            {
                failedScene = manager.LoadScene(fixture.SecondScenePath, LoadSceneMode.Additive);
                TestAssert.Require(harness.RuntimeCount == 1,
                    "A dynamically loaded Scene with a failed runtime start remained in the frame loop.");
            }
            finally
            {
                RuntimeSystemRegistry.Unregister(failingRegistration);
                if (failedScene is { isCreated: true } && manager.LoadedScenes.Contains(failedScene))
                    manager.UnloadScene(failedScene);
            }
        }
        finally
        {
            if (harness.IsPlaying) harness.ExitPlay();
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

    private static Fix64 DifferentFrom(Fix64 baseline, string preferred)
    {
        var value = Fix64.Parse(preferred);
        return value == baseline ? value + Fix64.One : value;
    }

    private readonly record struct RuntimeGlobalState(
        Fix64 DeltaTime,
        Fix64 UnscaledDeltaTime,
        Fix64 FixedDeltaTime,
        Fix64 TimeValue,
        Fix64 UnscaledTime,
        Fix64 FixedTime,
        Fix64 TimeScale,
        Fix64 MaximumDeltaTime,
        bool InFixedTimeStep,
        long FrameCount,
        int TargetFrameRate,
        int QualityLevel,
        int VSyncCount,
        int AntiAliasing,
        bool RealtimeReflectionProbes,
        Fix64 ShadowDistance,
        Resolution Resolution,
        FullScreenMode FullScreenMode,
        bool CursorVisible,
        CursorLockMode CursorLockState,
        BAsset? CursorTexture,
        Vector2 CursorHotspot,
        CursorMode CursorMode,
        BEngine.Random.State RandomState)
    {
        internal static RuntimeGlobalState Capture() => new(
            Time.deltaTime,
            Time.unscaledDeltaTime,
            Time.fixedDeltaTime,
            Time.time,
            Time.unscaledTime,
            Time.fixedTime,
            Time.timeScale,
            Time.maximumDeltaTime,
            Time.inFixedTimeStep,
            Time.frameCount,
            Application.targetFrameRate,
            QualitySettings.qualityLevel,
            QualitySettings.vSyncCount,
            QualitySettings.antiAliasing,
            QualitySettings.realtimeReflectionProbes,
            QualitySettings.shadowDistance,
            Screen.currentResolution,
            Screen.fullScreenMode,
            Cursor.visible,
            Cursor.lockState,
            Cursor.texture,
            Cursor.hotspot,
            Cursor.mode,
            BEngine.Random.state);

        internal void Restore()
        {
            SetTimeRuntimeValues(DeltaTime, UnscaledDeltaTime, TimeValue, UnscaledTime, FixedTime,
                InFixedTimeStep, FrameCount);
            Time.fixedDeltaTime = FixedDeltaTime;
            Time.timeScale = TimeScale;
            Time.maximumDeltaTime = MaximumDeltaTime;
            Application.targetFrameRate = TargetFrameRate;
            QualitySettings.SetQualityLevel(QualityLevel, false);
            QualitySettings.vSyncCount = VSyncCount;
            QualitySettings.antiAliasing = AntiAliasing;
            QualitySettings.realtimeReflectionProbes = RealtimeReflectionProbes;
            QualitySettings.shadowDistance = ShadowDistance;
            Screen.SetResolution(Resolution.width, Resolution.height, FullScreenMode, Resolution.refreshRate);
            Cursor.visible = CursorVisible;
            Cursor.lockState = CursorLockState;
            Cursor.SetCursor(CursorTexture, CursorHotspot, CursorMode);
            BEngine.Random.state = RandomState;
        }

        internal void RequireCurrent(string operation)
        {
            RequireEqual(DeltaTime, Time.deltaTime, operation, "Time.deltaTime");
            RequireEqual(UnscaledDeltaTime, Time.unscaledDeltaTime, operation, "Time.unscaledDeltaTime");
            RequireEqual(FixedDeltaTime, Time.fixedDeltaTime, operation, "Time.fixedDeltaTime");
            RequireEqual(TimeValue, Time.time, operation, "Time.time");
            RequireEqual(UnscaledTime, Time.unscaledTime, operation, "Time.unscaledTime");
            RequireEqual(FixedTime, Time.fixedTime, operation, "Time.fixedTime");
            RequireEqual(TimeScale, Time.timeScale, operation, "Time.timeScale");
            RequireEqual(MaximumDeltaTime, Time.maximumDeltaTime, operation, "Time.maximumDeltaTime");
            RequireEqual(InFixedTimeStep, Time.inFixedTimeStep, operation, "Time.inFixedTimeStep");
            RequireEqual(FrameCount, Time.frameCount, operation, "Time.frameCount");
            RequireEqual(TargetFrameRate, Application.targetFrameRate, operation, "Application.targetFrameRate");
            RequireEqual(QualityLevel, QualitySettings.qualityLevel, operation, "QualitySettings.qualityLevel");
            RequireEqual(VSyncCount, QualitySettings.vSyncCount, operation, "QualitySettings.vSyncCount");
            RequireEqual(AntiAliasing, QualitySettings.antiAliasing, operation, "QualitySettings.antiAliasing");
            RequireEqual(RealtimeReflectionProbes, QualitySettings.realtimeReflectionProbes, operation,
                "QualitySettings.realtimeReflectionProbes");
            RequireEqual(ShadowDistance, QualitySettings.shadowDistance, operation,
                "QualitySettings.shadowDistance");
            RequireEqual(Resolution, Screen.currentResolution, operation, "Screen.currentResolution");
            RequireEqual(FullScreenMode, Screen.fullScreenMode, operation, "Screen.fullScreenMode");
            RequireEqual(CursorVisible, Cursor.visible, operation, "Cursor.visible");
            RequireEqual(CursorLockState, Cursor.lockState, operation, "Cursor.lockState");
            TestAssert.Require(ReferenceEquals(CursorTexture, Cursor.texture),
                $"{operation} did not restore Cursor.texture.");
            RequireEqual(CursorHotspot, Cursor.hotspot, operation, "Cursor.hotspot");
            RequireEqual(CursorMode, Cursor.mode, operation, "Cursor.mode");
            RequireEqual(RandomState, BEngine.Random.state, operation, "Random.state");
        }

        internal void RequireDifferentFrom(RuntimeGlobalState baseline)
        {
            TestAssert.Require(DeltaTime != baseline.DeltaTime &&
                               UnscaledDeltaTime != baseline.UnscaledDeltaTime &&
                               FixedDeltaTime != baseline.FixedDeltaTime &&
                               TimeValue != baseline.TimeValue && UnscaledTime != baseline.UnscaledTime &&
                               FixedTime != baseline.FixedTime &&
                               TimeScale != baseline.TimeScale &&
                               MaximumDeltaTime != baseline.MaximumDeltaTime &&
                               InFixedTimeStep != baseline.InFixedTimeStep && FrameCount != baseline.FrameCount &&
                               TargetFrameRate != baseline.TargetFrameRate &&
                               QualityLevel != baseline.QualityLevel && VSyncCount != baseline.VSyncCount &&
                               AntiAliasing != baseline.AntiAliasing &&
                               RealtimeReflectionProbes != baseline.RealtimeReflectionProbes &&
                               ShadowDistance != baseline.ShadowDistance && Resolution != baseline.Resolution &&
                               FullScreenMode != baseline.FullScreenMode && CursorVisible != baseline.CursorVisible &&
                               CursorLockState != baseline.CursorLockState &&
                               !ReferenceEquals(CursorTexture, baseline.CursorTexture) &&
                               CursorHotspot != baseline.CursorHotspot && CursorMode != baseline.CursorMode &&
                               RandomState != baseline.RandomState,
                "The runtime-state fixture did not mutate every captured global setting.");
        }

        private static void RequireEqual<T>(T expected, T actual, string operation, string setting)
        {
            TestAssert.Require(EqualityComparer<T>.Default.Equals(expected, actual),
                $"{operation} did not restore {setting}: expected '{expected}', actual '{actual}'.");
        }
    }

    private static void SetTimeRuntimeValues(
        Fix64 deltaTime,
        Fix64 unscaledDeltaTime,
        Fix64 time,
        Fix64 unscaledTime,
        Fix64 fixedTime,
        bool inFixedTimeStep,
        long frameCount)
    {
        SetTimeField("_deltaTime", deltaTime);
        SetTimeField("_unscaledDeltaTime", unscaledDeltaTime);
        SetTimeField("_time", time);
        SetTimeField("_unscaledTime", unscaledTime);
        SetTimeField("_fixedTime", fixedTime);
        SetTimeField("_inFixedTimeStep", inFixedTimeStep);
        SetTimeField("_frameCount", frameCount);
    }

    private static void SetTimeField<T>(string name, T value)
    {
        var field = typeof(Time).GetField(name, BindingFlags.Static | BindingFlags.NonPublic) ??
                    throw new MissingFieldException(typeof(Time).FullName, name);
        field.SetValue(null, value);
    }
}

internal sealed class PlayModeTeardownSaveProbe : MonoBehaviour
{
    public static bool Enabled { get; set; }
    public static int DestroyCount { get; private set; }
    public static bool WasPlayingDuringDestroy { get; private set; }
    public static bool SaveResult { get; private set; }

    public static void ResetState()
    {
        Enabled = true;
        DestroyCount = 0;
        WasPlayingDuringDestroy = true;
        SaveResult = true;
    }

    public override void OnDestroy()
    {
        if (!Enabled || gameObject.scene is not { } scene) return;
        DestroyCount++;
        WasPlayingDuringDestroy &= Application.isPlaying && EditorApplication.isPlaying;
        EditorSceneManager.MarkSceneDirty(scene);
        SaveResult = EditorSceneManager.SaveScene(scene);
    }
}
