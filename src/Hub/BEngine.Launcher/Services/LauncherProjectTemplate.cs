using System.Text;
using BEngine.ProjectSystem;

namespace BEngine.Launcher;

// Captured from ProjectWorkspaceFactory. Serialized engine type names follow the
// selected release; system assembly versions retain the template's target runtime.
internal static class LauncherProjectTemplate
{
    internal static void Write(LauncherProjectWorkspace workspace)
    {
        var assemblyVersion = ResolveAssemblyVersion(workspace.Project.EngineVersion);
        foreach (var (relative, template) in Files)
        {
            var contents = template;
            for (var index = 0; index < 7; index++) contents = contents.Replace("$GUID" + index + "$", Guid.NewGuid().ToString());
            if (relative == "ProjectSettings/ProjectSettings.yaml")
                contents = contents.Replace("productName: TemplateProbe", "productName: " + YamlUtility.Serialize(workspace.Project.Name).TrimEnd('\n', '\r'));
            contents = contents.Replace("Version=0.1.0.0,", "Version=" + assemblyVersion + ",");
            var target = workspace.ResolveInside(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, contents + "\n", new UTF8Encoding(false));
        }
    }

    internal static string ResolveAssemblyVersion(string engineVersion)
    {
        BPackageVersion.Validate(engineVersion);
        var release = engineVersion.Split('-', '+')[0];
        var version = Version.Parse(release);
        if (version.Major > ushort.MaxValue || version.Minor > ushort.MaxValue || version.Build > ushort.MaxValue)
            throw new InvalidDataException($"Engine version '{engineVersion}' cannot be represented as an assembly version.");
        return new Version(version.Major, version.Minor, version.Build, 0).ToString(4);
    }

    private static readonly (string Path, string Contents)[] Files =
    [
        (".gitignore", """
        Library/
        Logs/
        Temp/
        """),
        ("Assets/Editor/ProjectMenus.cs", """
        using BEngine;
        using BEngine.Editor;
        
        namespace Game.Editor;
        
        public static class ProjectMenus
        {
            [MenuItem("Tools/Project/Log Selected Object", false, 100)]
            private static void LogSelectedObject()
            {
                Debug.Log($"Selected: {Selection.activeGameObject?.name}");
            }
        
            [MenuItem("Tools/Project/Log Selected Object", true)]
            private static bool ValidateLogSelectedObject() => Selection.activeGameObject is not null;
        }
        """),
        ("Assets/Scenes/Main.scene.yaml", """
        format: BEngine.Scene
        version: 2
        id: $GUID0$
        name: Main
        gameObjects:
        - id: $GUID1$
          name: Main Camera
          active: true
          tag: Untagged
          layer: 1
          isStatic: false
          transform:
            id: $GUID2$
            type: BEngine.Transform
            localPosition:
              x: "0"
              y: "0"
            localRotation: "0"
            localScale:
              x: "1"
              y: "1"
            fields: {}
          components:
          - id: $GUID3$
            type: BEngine.Camera2D
            enabled: true
            fields:
              backgroundColor: 0.05499999993480741977691650390625,0.0709999999962747097015380859375,0.0899999998509883880615234375,1
              clearMode: Color
              cullingMask: "9223372036854775807"
              isMain: "true"
              priority: "0"
              size: "5"
              viewportRect: 0,0,1,1
            graph:
              version: 1
              members:
              - name: backgroundColor
                value:
                  kind: Scalar
                  type: BEngine.Color, BEngine, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null
                  value: 0.05499999993480741977691650390625,0.0709999999962747097015380859375,0.0899999998509883880615234375,1
                  objectId: 0
                  referenceId: 0
                  dimensions: []
                  items: []
                  members: []
                  entries: []
              - name: clearMode
                value:
                  kind: Enum
                  type: BEngine.CameraClearMode, BEngine, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null
                  value: Color
                  objectId: 0
                  referenceId: 0
                  dimensions: []
                  items: []
                  members: []
                  entries: []
              - name: cullingMask
                value:
                  kind: Scalar
                  type: System.UInt64, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e
                  value: "9223372036854775807"
                  objectId: 0
                  referenceId: 0
                  dimensions: []
                  items: []
                  members: []
                  entries: []
              - name: isMain
                value:
                  kind: Scalar
                  type: System.Boolean, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e
                  value: "True"
                  objectId: 0
                  referenceId: 0
                  dimensions: []
                  items: []
                  members: []
                  entries: []
              - name: priority
                value:
                  kind: Scalar
                  type: BEngine.Fix64, BEngine, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null
                  value: "0"
                  objectId: 0
                  referenceId: 0
                  dimensions: []
                  items: []
                  members: []
                  entries: []
              - name: size
                value:
                  kind: Scalar
                  type: BEngine.Fix64, BEngine, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null
                  value: "5"
                  objectId: 0
                  referenceId: 0
                  dimensions: []
                  items: []
                  members: []
                  entries: []
              - name: targetTexture
                value:
                  kind: NullValue
                  type: BEngine.RenderTexture, BEngine, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null
                  value: ""
                  objectId: 0
                  referenceId: 0
                  dimensions: []
                  items: []
                  members: []
                  entries: []
              - name: viewportRect
                value:
                  kind: Scalar
                  type: BEngine.Rect, BEngine, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null
                  value: 0,0,1,1
                  objectId: 0
                  referenceId: 0
                  dimensions: []
                  items: []
                  members: []
                  entries: []
        - id: $GUID4$
          name: Sprite
          active: true
          tag: Untagged
          layer: 1
          isStatic: false
          transform:
            id: $GUID5$
            type: BEngine.Transform
            localPosition:
              x: "0"
              y: "0"
            localRotation: "0"
            localScale:
              x: "1"
              y: "1"
            fields: {}
          components:
          - id: $GUID6$
            type: BEngine.SpriteRenderer
            enabled: true
            fields:
              color: 1,1,1,1
              drawMode: Simple
              flipX: "false"
              flipY: "false"
              maskInteraction: None
              opacity: "1"
              orderInLayer: "0"
              pivot: 0.5,0.5
              receiveLighting: "true"
              runInEditMode: "false"
              size: 1,1
              sortingLayer: "1"
              useSpritePivot: "true"
            graph:
              version: 1
              members:
              - name: color
                value:
                  kind: Scalar
                  type: BEngine.Color, BEngine, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null
                  value: 1,1,1,1
                  objectId: 0
                  referenceId: 0
                  dimensions: []
                  items: []
                  members: []
                  entries: []
              - name: drawMode
                value:
                  kind: Enum
                  type: BEngine.SpriteDrawMode, BEngine, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null
                  value: Simple
                  objectId: 0
                  referenceId: 0
                  dimensions: []
                  items: []
                  members: []
                  entries: []
              - name: flipX
                value:
                  kind: Scalar
                  type: System.Boolean, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e
                  value: "False"
                  objectId: 0
                  referenceId: 0
                  dimensions: []
                  items: []
                  members: []
                  entries: []
              - name: flipY
                value:
                  kind: Scalar
                  type: System.Boolean, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e
                  value: "False"
                  objectId: 0
                  referenceId: 0
                  dimensions: []
                  items: []
                  members: []
                  entries: []
              - name: maskInteraction
                value:
                  kind: Enum
                  type: BEngine.SpriteMaskInteraction, BEngine, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null
                  value: None
                  objectId: 0
                  referenceId: 0
                  dimensions: []
                  items: []
                  members: []
                  entries: []
              - name: material
                value:
                  kind: EngineObject
                  type: BEngine.Material, BEngine, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null
                  value: builtin-material:BEngine%2FSprite|Default%20Sprite%20Material
                  objectId: 0
                  referenceId: 0
                  dimensions: []
                  items: []
                  members: []
                  entries: []
              - name: opacity
                value:
                  kind: Scalar
                  type: BEngine.Fix64, BEngine, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null
                  value: "1"
                  objectId: 0
                  referenceId: 0
                  dimensions: []
                  items: []
                  members: []
                  entries: []
              - name: orderInLayer
                value:
                  kind: Scalar
                  type: System.Int32, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e
                  value: "0"
                  objectId: 0
                  referenceId: 0
                  dimensions: []
                  items: []
                  members: []
                  entries: []
              - name: pivot
                value:
                  kind: Scalar
                  type: BEngine.Vector2, BEngine, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null
                  value: 0.5,0.5
                  objectId: 0
                  referenceId: 0
                  dimensions: []
                  items: []
                  members: []
                  entries: []
              - name: receiveLighting
                value:
                  kind: Scalar
                  type: System.Boolean, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e
                  value: "True"
                  objectId: 0
                  referenceId: 0
                  dimensions: []
                  items: []
                  members: []
                  entries: []
              - name: runInEditMode
                value:
                  kind: Scalar
                  type: System.Boolean, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e
                  value: "False"
                  objectId: 0
                  referenceId: 0
                  dimensions: []
                  items: []
                  members: []
                  entries: []
              - name: size
                value:
                  kind: Scalar
                  type: BEngine.Vector2, BEngine, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null
                  value: 1,1
                  objectId: 0
                  referenceId: 0
                  dimensions: []
                  items: []
                  members: []
                  entries: []
              - name: sortingLayer
                value:
                  kind: Scalar
                  type: System.UInt64, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e
                  value: "1"
                  objectId: 0
                  referenceId: 0
                  dimensions: []
                  items: []
                  members: []
                  entries: []
              - name: sprite
                value:
                  kind: NullValue
                  type: BEngine.Sprite, BEngine, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null
                  value: ""
                  objectId: 0
                  referenceId: 0
                  dimensions: []
                  items: []
                  members: []
                  entries: []
              - name: useSpritePivot
                value:
                  kind: Scalar
                  type: System.Boolean, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e
                  value: "True"
                  objectId: 0
                  referenceId: 0
                  dimensions: []
                  items: []
                  members: []
                  entries: []
        prefabInstances: []
        """),
        ("Assets/Scripts/Game.asmdef.yaml", """
        format: BEngine.AssemblyDefinition
        version: 1
        name: Game
        rootNamespace: Game
        references: []
        includePlatforms: []
        excludePlatforms: []
        defineConstraints: []
        autoReferenced: true
        editorOnly: false
        allowUnsafeCode: false
        """),
        ("Assets/Scripts/Rotator.cs", """
        using BEngine;
        
        namespace Game;
        
        public sealed class Rotator : MonoBehaviour
        {
            public Fix64 degreesPerSecond { get; set; } = 45;
        
            public override void Update()
            {
                transform.localRotation += degreesPerSecond * Time.deltaTime;
            }
        }
        """),
        ("Packages/manifest.yaml", """
        format: BEngine.Packages
        version: 1
        packages: []
        """),
        ("ProjectSettings/EditorSettings.yaml", """
        format: BEngine.EditorSettings
        version: 1
        lastScene: Assets/Scenes/Main.scene.yaml
        """),
        ("ProjectSettings/ProjectSettings.yaml", """
        format: BEngine.ProjectSettings
        version: 3
        locale: zh-CN
        companyName: DefaultCompany
        productName: TemplateProbe
        defaultScreenWidth: 1280
        defaultScreenHeight: 720
        fullScreen: false
        runInBackground: false
        graphicsBackend: Auto
        scriptingDefineSymbols: []
        tags:
        - Untagged
        - Respawn
        - Finish
        - EditorOnly
        - MainCamera
        - Player
        - GameController
        sortingLayers:
        - value: 1
          name: Default
          builtIn: true
          isUi: false
          builtInId: Default
        - value: 2
          name: TransparentFX
          builtIn: true
          isUi: false
          builtInId: TransparentFX
        - value: 3
          name: Ignore Raycast
          builtIn: true
          isUi: false
          builtInId: IgnoreRaycast
        - value: 4
          name: Water
          builtIn: true
          isUi: false
          builtInId: Water
        - value: 5
          name: UI
          builtIn: true
          isUi: true
          builtInId: UI
        """),
    ];
}
