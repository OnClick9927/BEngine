using System.Security.Cryptography;
using Microsoft.CodeAnalysis.CSharp;

namespace BEngine.ProjectSystem;

/// <summary>Relates transformed compiler snapshots back to their original source documents.</summary>
internal static class ScriptDebugSource
{
    internal const string Revision = "BEngine.ScriptDebugSource.v3";

    internal static string Map(string transformed, byte[] originalBytes, string originalPath)
    {
        var path = SyntaxFactory.Literal(originalPath).Text;
        var checksum = Convert.ToHexString(SHA256.HashData(originalBytes));
        // The external document has its original checksum even when loop guards
        // or a source processor changed the compiler snapshot's bytes.
        return $"#pragma checksum {path} \"{{8829d00f-11b8-4213-878b-770e8597ac16}}\" \"{checksum}\"\n#line 1 {path}\n{transformed}";
    }
}
