namespace BEngine.Editor;

/// <summary>Stable resource paths for the built-in Unity-style editor icon set.</summary>
public static class EditorBuiltinIcons
{
    private static readonly Dictionary<string, string> UnityNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GameObject Icon"] = Components.GameObject,
        ["Transform Icon"] = Components.Transform,
        ["Camera Icon"] = Components.Camera2D,
        ["MonoBehaviour Icon"] = Components.Script,
        ["SpriteRenderer Icon"] = Components.SpriteRenderer,
        ["ParticleSystem Icon"] = Components.ParticleSystem2D,
        ["Rigidbody2D Icon"] = Components.Rigidbody2D,
        ["Collider2D Icon"] = Components.Collider2D,
        ["BoxCollider2D Icon"] = Components.BoxCollider2D,
        ["CircleCollider2D Icon"] = Components.CircleCollider2D,
        ["CapsuleCollider2D Icon"] = Components.CapsuleCollider2D,
        ["PolygonCollider2D Icon"] = Components.PolygonCollider2D,
        ["Animator Icon"] = Components.Animator,
        ["Animation Icon"] = Components.Animation,
        ["Tilemap Icon"] = Components.Tilemap,
        ["TilemapRenderer Icon"] = Components.TilemapRenderer,
        ["UIDocument Icon"] = Components.UIDocument,
        ["NavMeshAgent Icon"] = Components.Navigation,
        ["cs Script Icon"] = Assets.Script,
        ["Folder Icon"] = Assets.FolderClosed,
        ["FolderOpened Icon"] = Assets.FolderOpen,
        ["DefaultAsset Icon"] = Assets.Default,
        ["SceneAsset Icon"] = Assets.Scene,
        ["Prefab Icon"] = Assets.Prefab,
        ["PrefabModel Icon"] = Assets.Prefab,
        ["Material Icon"] = Assets.Material,
        ["Shader Icon"] = Assets.Shader,
        ["Texture Icon"] = Assets.Image,
        ["Texture2D Icon"] = Assets.Image,
        ["RenderTexture Icon"] = Assets.Image,
        ["Sprite Icon"] = Assets.Image,
        ["AudioClip Icon"] = Assets.Audio,
        ["AnimationClip Icon"] = Assets.Animation,
        ["AnimatorController Icon"] = Assets.Animation,
        ["SpriteAtlas Icon"] = Assets.Atlas,
        ["GUISkin Icon"] = Assets.Skin,
        ["ScriptableObject Icon"] = Assets.Data,
        ["TextAsset Icon"] = Assets.Text,
        ["Font Icon"] = Assets.Font,
        ["AssemblyDefinitionAsset Icon"] = Assets.Assembly,
        ["SceneViewTools"] = Toolbar.View,
        ["ViewTool"] = Toolbar.View,
        ["MoveTool"] = Toolbar.Move,
        ["RotateTool"] = Toolbar.Rotate,
        ["ScaleTool"] = Toolbar.Scale,
        ["RectTool"] = Toolbar.Rect,
        ["PlayButton"] = Toolbar.Play,
        ["PauseButton"] = Toolbar.Pause,
        ["StepButton"] = Toolbar.Step,
        ["Toolbar Plus"] = Toolbar.Add,
        ["Toolbar Plus More"] = Toolbar.AddDropdown,
        ["Toolbar Minus"] = Toolbar.Minus,
        ["Search Icon"] = Toolbar.Search,
        ["SearchCancelButton"] = Toolbar.Close,
        ["ToolbarSearchCancelButton"] = Toolbar.Close,
        ["Close"] = Toolbar.Close,
        ["PaneOptions"] = Toolbar.More,
        ["_Help"] = Toolbar.Info,
        ["console.infoicon"] = Toolbar.Info,
        ["console.warnicon"] = Toolbar.Warning,
        ["console.erroricon"] = Toolbar.Error,
        ["Undo"] = Toolbar.Undo,
        ["Redo"] = Toolbar.Redo,
        ["UndoHistory"] = Toolbar.UndoHistory,
        ["History"] = Toolbar.UndoHistory,
        ["Layout"] = Toolbar.Layout,
        ["FrameDebugger"] = Toolbar.FrameDebugger,
        ["FoldoutClosed"] = Toolbar.FoldoutClosed,
        ["FoldoutOpen"] = Toolbar.FoldoutOpen
    };

    public static string Resolve(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.StartsWith("d_", StringComparison.OrdinalIgnoreCase) ? name[2..] : name;
        return UnityNames.GetValueOrDefault(normalized, name);
    }

    public static class Assets
    {
        public const string Animation = "Icons/Assets/AssetAnimation.png";
        public const string Atlas = "Icons/Assets/AssetAtlas.png";
        public const string Assembly = "Icons/Assets/AssetAssembly.png";
        public const string Audio = "Icons/Assets/AssetAudio.png";
        public const string Data = "Icons/Assets/AssetData.png";
        public const string Default = "Icons/Assets/AssetDefault.png";
        public const string Font = "Icons/Assets/AssetFont.png";
        public const string Image = "Icons/Assets/AssetImage.png";
        public const string Markup = "Icons/Assets/AssetMarkup.png";
        public const string Material = "Icons/Assets/AssetMaterial.png";
        public const string Model = "Icons/Assets/AssetModel.png";
        public const string Prefab = "Icons/Assets/AssetPrefab.png";
        public const string Scene = "Icons/Assets/AssetScene.png";
        public const string Script = "Icons/Assets/AssetScript.png";
        public const string Shader = "Icons/Assets/AssetShader.png";
        public const string Skin = "Icons/Assets/AssetSkin.png";
        public const string Style = "Icons/Assets/AssetStyle.png";
        public const string Text = "Icons/Assets/AssetText.png";
        public const string FolderClosed = "Icons/Assets/FolderClosed.png";
        public const string FolderEmpty = "Icons/Assets/FolderEmpty.png";
        public const string FolderOpen = "Icons/Assets/FolderOpen.png";
    }

    public static class Components
    {
        public const string Default = "Icons/Components/Component.png";
        public const string GameObject = "Icons/Components/GameObject.png";
        public const string Transform = "Icons/Components/Transform.png";
        public const string Camera2D = "Icons/Components/Camera.png";
        public const string Script = "Icons/Components/Script.png";
        public const string SpriteRenderer = "Icons/Components/SpriteRenderer.png";
        public const string LineRenderer2D = SpriteRenderer;
        public const string TrailRenderer2D = SpriteRenderer;
        public const string SpriteMask = SpriteRenderer;
        public const string ParticleSystem2D = "Icons/Components/ParticleSystem2D.png";
        public const string Rigidbody2D = "Icons/Components/Rigidbody2D.png";
        public const string Collider2D = "Icons/Components/Collider2D.png";
        public const string BoxCollider2D = Collider2D;
        public const string CircleCollider2D = Collider2D;
        public const string CapsuleCollider2D = Collider2D;
        public const string PolygonCollider2D = Collider2D;
        public const string Animator = "Icons/Components/Animator.png";
        public const string Animation = Animator;
        public const string Tilemap = "Icons/Components/Tilemap.png";
        public const string TilemapRenderer = Tilemap;
        public const string UIDocument = "Icons/Components/UIDocument.png";
        public const string Navigation = "Icons/Components/Navigation.png";
    }

    public static class Toolbar
    {
        public const string Add = "Icons/Toolbar/Add.png";
        public const string AddDropdown = "Icons/Toolbar/AddDropdown.png";
        public const string Minus = "Icons/Toolbar/Minus.png";
        public const string New = "Icons/Toolbar/New.png";
        public const string Save = "Icons/Toolbar/Save.png";
        public const string Delete = "Icons/Toolbar/Delete.png";
        public const string Search = "Icons/Toolbar/Search.png";
        public const string Clear = "Icons/Toolbar/Clear.png";
        public const string Close = "Icons/Toolbar/Close.png";
        public const string Undo = "Icons/Toolbar/Undo.png";
        public const string Redo = "Icons/Toolbar/Redo.png";
        public const string UndoHistory = "Icons/Toolbar/UndoHistory.png";
        public const string Layout = "Icons/Toolbar/Layout.png";
        public const string FrameDebugger = "Icons/Toolbar/FrameDebugger.png";
        public const string Dropdown = "Icons/Toolbar/Dropdown.png";
        public const string Filter = "Icons/Toolbar/Filter.png";
        public const string Import = "Icons/Toolbar/Import.png";
        public const string Export = "Icons/Toolbar/Export.png";
        public const string Duplicate = "Icons/Toolbar/Duplicate.png";
        public const string Refresh = "Icons/Toolbar/Refresh.png";
        public const string Browse = "Icons/Toolbar/Browse.png";
        public const string OpenFolder = "Icons/Toolbar/OpenFolder.png";
        public const string Settings = "Icons/Toolbar/Settings.png";
        public const string Lock = "Icons/Toolbar/Lock.png";
        public const string Unlock = "Icons/Toolbar/Unlock.png";
        public const string Visible = "Icons/Toolbar/Visible.png";
        public const string Hidden = "Icons/Toolbar/Hidden.png";
        public const string Info = "Icons/Toolbar/Info.png";
        public const string Warning = "Icons/Toolbar/Warning.png";
        public const string Error = "Icons/Toolbar/Error.png";
        public const string Check = "Icons/Toolbar/Check.png";
        public const string Send = "Icons/Toolbar/Send.png";
        public const string Html = "Icons/Toolbar/Html.png";
        public const string Container = "Icons/Toolbar/Container.png";
        public const string Label = "Icons/Toolbar/Label.png";
        public const string Button = "Icons/Toolbar/Button.png";
        public const string Field = "Icons/Toolbar/Field.png";
        public const string Up = "Icons/Toolbar/Up.png";
        public const string View = "Icons/Toolbar/View.png";
        public const string Move = "Icons/Toolbar/Move.png";
        public const string Rotate = "Icons/Toolbar/Rotate.png";
        public const string Scale = "Icons/Toolbar/Scale.png";
        public const string Rect = "Icons/Toolbar/Rect.png";
        public const string Play = "Icons/Toolbar/Play.png";
        public const string Stop = "Icons/Toolbar/Stop.png";
        public const string Pause = "Icons/Toolbar/Pause.png";
        public const string Step = "Icons/Toolbar/Step.png";
        public const string FoldoutClosed = "Icons/Toolbar/FoldoutClosed.png";
        public const string FoldoutOpen = "Icons/Toolbar/FoldoutOpen.png";
        public const string More = "Icons/Toolbar/More.png";
    }
}
