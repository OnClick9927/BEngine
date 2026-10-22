namespace BEngine.Editor;

public readonly record struct BPackageProgress(
    BPackageProgressPhase Phase,
    int Completed,
    int Total,
    string Path)
{
    public float Fraction => Total <= 0 ? 1f : Math.Clamp((float)Completed / Total, 0f, 1f);
}
