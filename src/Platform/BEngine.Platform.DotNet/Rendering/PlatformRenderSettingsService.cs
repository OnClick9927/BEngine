namespace BEngine.Platform;

public sealed class PlatformRenderSettingsService : IRenderSettingsService
{
    private static readonly string[] Names = ["Very Low", "Low", "Medium", "High", "Very High", "Ultra"];
    private static readonly Fix64 MinimumRenderScale = Fix64.FromDecimal(0.25m);
    private static readonly Fix64 MaximumRenderScale = Fix64.One;
    public int qualityLevel { get; private set; } = 3;
    public string[] names => [.. Names];
    private int _vSyncCount = 1;
    private int _antiAliasing;
    public int targetFrameRate { get; set; } = -1;
    private Fix64 _renderScale = Fix64.One;
    public Fix64 renderScale
    {
        get => _renderScale;
        set
        {
            // Quantizing avoids reallocating the post-process target for tiny
            // adaptive changes and keeps the number of GPU allocations bounded.
            var clamped = Fix64.Clamp(value, MinimumRenderScale, MaximumRenderScale);
            _renderScale = (Fix64)Mathf.RoundToInt(clamped * 8) / 8;
        }
    }
    public int vSyncCount
    {
        get => _vSyncCount;
        set => _vSyncCount = value is >= 0 and <= 4 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }
    public int antiAliasing
    {
        get => _antiAliasing;
        set => _antiAliasing = value is 0 or 2 or 4 or 8 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }
    public bool realtimeReflectionProbes { get; set; } = true;
    public Fix64 shadowDistance { get; set; } = 50;
    public void SetQualityLevel(int index, bool applyExpensiveChanges = true) =>
        qualityLevel = Math.Clamp(index, 0, Names.Length - 1);
    public void IncreaseLevel(bool applyExpensiveChanges = false) => SetQualityLevel(qualityLevel + 1);
    public void DecreaseLevel(bool applyExpensiveChanges = false) => SetQualityLevel(qualityLevel - 1);
}
