namespace BEngine.AssetBundles;
internal static class AssetBundleVersionLabel
{
 internal static string NormalizePortable(string? value,string fieldName)=>BEngine.Player.ContentVersionLabel.NormalizePortable(value,fieldName);
 internal static void ValidatePortable(string value,string fieldName)=>BEngine.Player.ContentVersionLabel.ValidatePortable(value,fieldName);
 internal static string NormalizeHotResourceVersion(string? value)=>BEngine.Player.ContentVersionLabel.NormalizeHotResourceVersion(value);
 internal static int CompareHotResourceVersions(string left,string right)=>BEngine.Player.ContentVersionLabel.CompareHotResourceVersions(left,right);
}
