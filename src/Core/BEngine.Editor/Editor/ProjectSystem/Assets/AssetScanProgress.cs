using System.Security.Cryptography;
using BEngine.Serialization;

namespace BEngine.ProjectSystem.Editor;

public readonly record struct AssetScanProgress(int Completed, int Total, string AssetPath)
{
    public float Ratio => Total <= 0 ? 1f : (float)Completed / Total;
}
