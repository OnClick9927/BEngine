using BEngine.DependencyInjection;
using BEngine.Editor.Rendering;
using System.Runtime.CompilerServices;
namespace BEngine.Editor;

internal static class IMGUIEditorModule
{
    [ModuleInitializer]
    internal static void Register()
    {
        BEngine.ProjectSystem.PackageEditorTemplates.Register(PackageWindowTemplate.Create);
        EditorFramePresenters.Factory = static device => new IMGUIFramePresenter(device);
        EditorInputRouting.HotControlProvider = static () => GUIUtility.hotControl;
        EditorInputRouting.KeyboardControlProvider = static () => GUIUtility.keyboardControl;
        EditorFeatureIsolation.Current = new Isolation();
        EditorPreferenceAccess.ExternalCodeEditorProvider = static () => EditorPreferences.current.ExternalScriptEditor ?? string.Empty;
        EditorBuiltinIcons.IsDarkTheme = static () => EditorAppearance.isDarkTheme;
        EditorExtensionLifecycle.Invalidated += PropertyDrawerRegistry.Invalidate;
        TypeCache.RegisterWarmup("PropertyDrawers", PropertyDrawerRegistry.Warmup);
        TypeCache.RegisterWarmup("Inspectors", EditorTypeRegistry.Warmup);
        EditorExtensionLifecycle.Invalidated += AssetPreviewProviders.Invalidate;
        EditorApplication.projectChanged += EditorWindow.NotifyProjectChanged;
        EditorApplication.hierarchyChanged += EditorWindow.NotifyHierarchyChanged;
        Selection.selectionChanged += EditorWindow.NotifySelectionChanged;
        EditorPresentationRequests.PingRequested += EditorObjectPing.Ping;
        EditorPresentationRequests.PopupMenuRequested += ShowPopup;
        AssetTypeRegistry.Register<GUISkin>(".guiskin.yaml", "GUISkin", EditorBuiltinIcons.Assets.Default);
        RuntimeAssemblyCleanup.Register(static assembly =>
        {
            // Providers can come from any extension, not only this UI assembly.
            // Clear before the owner unloads, including package-load rollback paths.
            AssetPreviewProviders.Invalidate();
            if (assembly != typeof(IMGUIEditorModule).Assembly) return;
            EditorFramePresenters.Factory = null;
            EditorInputRouting.HotControlProvider = null;
            EditorInputRouting.KeyboardControlProvider = null;
            EditorFeatureIsolation.Current = null;
            EditorPreferenceAccess.ExternalCodeEditorProvider = null;
            EditorExtensionLifecycle.Invalidated -= PropertyDrawerRegistry.Invalidate;
            EditorExtensionLifecycle.Invalidated -= AssetPreviewProviders.Invalidate;
            AssetPreviewProviders.Invalidate();
            EditorApplication.projectChanged -= EditorWindow.NotifyProjectChanged;
            EditorApplication.hierarchyChanged -= EditorWindow.NotifyHierarchyChanged;
            Selection.selectionChanged -= EditorWindow.NotifySelectionChanged;
            EditorPresentationRequests.PingRequested -= EditorObjectPing.Ping;
            EditorPresentationRequests.PopupMenuRequested -= ShowPopup;
            PropertyDrawerRegistry.Invalidate();
            AssetPreview.ClearTemporaryAssetPreviews();
        });
    }
    private static void ShowPopup(Rect area,string path,MenuCommand command)
    {
        var menu = new GenericMenu();
        MenuItemRegistry.Discover().PopulatePath(menu,path,command.context);
        menu.DropDown(area);
    }
    private sealed class Isolation : IEditorFeatureIsolation
    {
        private GUI.FeatureIsolationScope[] _scopes = new GUI.FeatureIsolationScope[8];
        private int _count;
        public int Begin()
        {
            if (_count == _scopes.Length) Array.Resize(ref _scopes, _count * 2);
            _scopes[_count] = GUI.BeginFeatureIsolation();
            return _count++;
        }
        public void Restore(int token, bool succeeded)
        {
            _count = token;
            var scope = _scopes[token];
            _scopes[token] = default;
            scope.Restore(succeeded);
        }
    }
}
