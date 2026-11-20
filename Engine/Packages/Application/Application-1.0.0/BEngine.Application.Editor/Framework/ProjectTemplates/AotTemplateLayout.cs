using BEngine.Editor.Build.ProjectSystem;

namespace BEngine.App.Editor.ProjectSystem;

/// <summary>Assets supplied by the quick-start framework, independent of the engine's AOT identity.</summary>
public static class AotTemplateLayout
{
    public const string UiAssetRoot = AotProjectLayout.AssetRoot + "/UI";
    public const string UiDocumentAssetPath = UiAssetRoot + "/AOT.bxml";
    public const string UiStyleAssetPath = UiAssetRoot + "/AOT.bss";
    public const string UiControllerAssetPath = UiAssetRoot + "/AotStartupView.cs";
    public const string LogoAssetPath = UiAssetRoot + "/BEngine.png";
}
