namespace BEngine.Cinemachine;

[CreateAssetMenu(fileName = "Cinemachine Blends", menuName = "Cinemachine/Blender Settings", order = 300)]
public sealed class CinemachineBlenderSettings : ScriptableObject
{
    public List<CustomBlend> customBlends { get; set; } = [];

    public CinemachineBlendDefinition GetBlendFor(
        CinemachineCamera? from,
        CinemachineCamera to,
        CinemachineBlendDefinition fallback)
    {
        ArgumentNullException.ThrowIfNull(to);
        var fromName = from?.gameObject.name ?? string.Empty;
        return customBlends.FirstOrDefault(item =>
            NameMatches(item.from, fromName) && NameMatches(item.to, to.gameObject.name))?.blend ?? fallback;
    }

    private static bool NameMatches(string pattern, string value) =>
        pattern == "*" || string.Equals(pattern, value, StringComparison.Ordinal);

    public sealed class CustomBlend
    {
        public string from { get; set; } = "*";
        public string to { get; set; } = "*";
        public CinemachineBlendDefinition blend { get; set; } =
            new(CinemachineBlendStyle.EaseInOut, Fix64.One);
    }
}
