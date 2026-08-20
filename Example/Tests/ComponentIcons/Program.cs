using BEngine.Editor;
using BEngine.Editor.Rendering;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.ExampleTests.ComponentIcons;

internal static class Program
{
    private static int Main()
    {
        var repository = FindRepositoryRoot();
        Resources.RegisterResourceRoot(Path.Combine(repository, "src", "Core"));

        Require(EditorIconRegistry.GetComponentIconPath(typeof(Camera2D)).EndsWith(
            "Icons/Components/Camera.png", StringComparison.OrdinalIgnoreCase),
            "Camera2D did not receive the built-in component icon.");
        Require(EditorIconRegistry.GetComponentIconPath(typeof(SpriteRenderer)).EndsWith(
            "Icons/Assets/AssetImage.png", StringComparison.OrdinalIgnoreCase),
            "SpriteRenderer did not receive the image icon.");
        Require(EditorIconRegistry.GetComponentIconPath(typeof(ExampleBehaviour)).EndsWith(
            "Icons/Components/Script.png", StringComparison.OrdinalIgnoreCase),
            "MonoBehaviour did not receive the script icon.");

        EditorIconRegistry.Register(typeof(ExampleBehaviour), "Icons/Assets/AssetMaterial.png");
        var overridden = EditorIconRegistry.GetComponentIconPath(typeof(ExampleBehaviour));
        Require(File.Exists(overridden) && overridden.EndsWith("AssetMaterial.png", StringComparison.OrdinalIgnoreCase),
            "A package component icon registration did not override the default icon.");

        Require(FileGpuCanvasResourceResolver.Shared.TryResolveTexture(
                "Icons/Components/Script.png", out var texture),
            "The GPU Canvas did not resolve the component PNG from EditorResources.");
        Require(texture.Width > 0 && texture.Height > 0 &&
                texture.Format == GraphicsTextureFormat.Rgba8Unorm &&
                texture.Pixels.Length == texture.Width * texture.Height * 4,
            "The component icon was not decoded as an RGBA GPU texture.");
        Require(EditorGUIUtility.IconContent("d_Camera Icon").image == EditorBuiltinIcons.Components.Camera2D &&
                EditorGUIUtility.IconContent("cs Script Icon").image == EditorBuiltinIcons.Assets.Script,
            "Unity-style icon names were not resolved to the BEngine icon set.");

        Console.WriteLine("COMPONENT_ICONS_OK|defaults,package-override,png-rgba,unity-aliases");
        return 0;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("BEngine repository root was not found.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ExampleBehaviour : MonoBehaviour;
}
