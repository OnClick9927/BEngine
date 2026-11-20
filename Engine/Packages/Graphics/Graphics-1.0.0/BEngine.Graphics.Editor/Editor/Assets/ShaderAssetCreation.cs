namespace BEngine.Editor;
internal static class ShaderAssetCreation
{
    [MenuItem("Assets/Create/Shader", false, 200)]
    private static void CreateFromMenu() => ProjectAssetCreation.CreateAndReveal(CreateShader);
    [MenuItem("Assets/Create/Shader", true)]
    private static bool Validate() => ProjectAssetCreation.CanCreateFromMenu();
    private static string CreateShader(string folder)
    {
        var path = ProjectAssetCreation.Unique(folder, "New Shader.shader");
        ProjectAssetCreation.WriteText(path, """
            #pragma stage fragment
            float4 main() : SV_Target
            {
                return float4(1.0, 1.0, 1.0, 1.0);
            }
            """);
        return path;
    }

}
