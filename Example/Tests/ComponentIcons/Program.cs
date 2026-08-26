using BEngine.Editor;
using BEngine.Editor.Rendering;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.ExampleTests.ComponentIcons;

internal static class Program
{
    private static int Main()
    {
        try
        {
            var repository = FindRepositoryRoot();
            EditorResource.RegisterResourceRoot(Path.Combine(repository, "src", "Core"));

            Require(EditorIconRegistry.GetComponentIconPath(typeof(Camera2D)).EndsWith(
                    EditorBuiltinIcons.Components.Camera2D, StringComparison.OrdinalIgnoreCase),
                "Camera2D did not receive the built-in component icon.");
            Require(EditorIconRegistry.GetComponentIconPath(typeof(ExampleBehaviour)).EndsWith(
                    EditorBuiltinIcons.Components.Script, StringComparison.OrdinalIgnoreCase),
                "MonoBehaviour did not receive the script icon.");
            Require(EditorIconRegistry.GetComponentIconPath(typeof(Transform)).EndsWith(
                    EditorBuiltinIcons.Components.Transform, StringComparison.OrdinalIgnoreCase),
                "Transform did not receive the transform icon.");

            var rendererIcon = EditorIconRegistry.GetComponentIconPath(typeof(SpriteRenderer));
            Require(ResourcePathEndsWith(rendererIcon, EditorBuiltinIcons.Assets.Image),
                "SpriteRenderer did not receive the built-in image icon.");

            EditorIconRegistry.Register(typeof(ExampleBehaviour), EditorBuiltinIcons.Assets.Material);
            var overridden = EditorIconRegistry.GetComponentIconPath(typeof(ExampleBehaviour));
            Require(File.Exists(overridden) && ResourcePathEndsWith(
                    overridden, EditorBuiltinIcons.Assets.Material),
                "A package component icon registration did not override the default icon.");

            Require(FileGpuCanvasResourceResolver.Shared.TryResolveTexture(
                    EditorBuiltinIcons.Components.Script, out var texture),
                "The GPU Canvas did not resolve the component PNG from Editor.");
            Require(texture.Width > 0 && texture.Height > 0 &&
                    texture.Format == GraphicsTextureFormat.Rgba8Unorm &&
                    texture.Pixels.Length == texture.Width * texture.Height * 4,
                "The component icon was not decoded as an RGBA GPU texture.");
            Require(EditorGUIUtility.IconContent("d_Camera Icon").image ==
                    EditorBuiltinIcons.Components.Camera2D &&
                    EditorGUIUtility.IconContent("cs Script Icon").image == EditorBuiltinIcons.Assets.Script,
                "Unity-style icon names were not resolved to the BEngine icon set.");

            Console.WriteLine(
                "COMPONENT_ICONS_OK|type-defaults,sprite-image,package-override,png-rgba,unity-aliases");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"COMPONENT_ICONS_FAILED|{exception}");
            return 1;
        }
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

    private static bool ResourcePathEndsWith(string path, string resourcePath) =>
        path.Replace('\\', '/').EndsWith(resourcePath, StringComparison.OrdinalIgnoreCase);

    private sealed class ExampleBehaviour : MonoBehaviour;
}
