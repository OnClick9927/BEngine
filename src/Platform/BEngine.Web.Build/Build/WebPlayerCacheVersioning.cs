using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BEngine.Platform;

namespace BEngine.Editor;

internal static class WebPlayerCacheVersioning
{
    internal static void Apply(string webRoot, FileUtility files, PathUtility paths)
    {
        var bytes = files.ReadAllBytes(paths.Combine(webRoot, "player.bin"));
        var hash = SHA256.HashData(bytes);
        var indexPath = paths.Combine(webRoot, "index.html");
        var index = files.ReadAllText(indexPath);
        var reference = new Regex("data-bengine-main=\"\\./player\\.bin(?:\\?benginev=[0-9a-f]{64})?\"", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var integrity = new Regex("data-bengine-integrity=\"(?:BENGINE_PLAYER_INTEGRITY|sha256-[A-Za-z0-9+/]{43}=)\"", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (reference.Matches(index).Count != 1 || integrity.Matches(index).Count != 1)
            throw new InvalidDataException("The Web player entry must contain one compressed player reference and integrity value.");
        index = reference.Replace(index, _ => "data-bengine-main=\"./player.bin?benginev=" + Convert.ToHexStringLower(hash) + "\"");
        index = integrity.Replace(index, _ => "data-bengine-integrity=\"sha256-" + Convert.ToBase64String(hash) + "\"");
        files.WriteAllText(indexPath, index);
    }
}
