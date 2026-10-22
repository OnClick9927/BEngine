namespace BEngine.Localization;

[AddComponentMenu("Localization/Localization Controller")]
[DisallowMultipleComponent]
public sealed class LocalizationController : MonoBehaviour
{
    public string defaultLanguage { get; set; } = "en";
    public string preferenceKey { get; set; } = "BEngine.Localization.Language";
    public bool rememberLanguage { get; set; } = true;
    public List<LocalizationTable> tables { get; set; } = [];
    private LocalizationContext? _context;
    [HideInInspector, YamlDotNet.Serialization.YamlIgnore]
    public LocalizationContext context
    {
        get
        {
            if (_context is not null) return _context;
            var created = new LocalizationContext(defaultLanguage);
            try
            {
                foreach (var table in tables) created.MergeContext(table);
                if (rememberLanguage) created.recorder = new PlayerPrefsLocalizationRecorder(preferenceKey);
                created.InitializeAsync(defaultLanguage).GetAwaiter().GetResult();
                return _context = created;
            }
            catch { created.Dispose(); throw; }
        }
    }
    public override void Awake() => _ = context;
    public override void OnDestroy() { _context?.Dispose(); _context = null; }
}

[AddComponentMenu("Localization/Localized Property")]
public sealed class LocalizedProperty : MonoBehaviour
{
    public LocalizationController? controller { get; set; }
    public BObject? target { get; set; }
    public string member { get; set; } = string.Empty;
    public string key { get; set; } = string.Empty;
    private LocalizationBinding? _binding;
    public override void OnEnable() => RefreshBinding();
    public override void OnDisable() { _binding?.Dispose(); _binding = null; }
    public override void OnDestroy() => OnDisable();
    [ContextMenu("Refresh Localization")]
    public void RefreshBinding()
    {
        OnDisable();
        if (controller is null || target is null || key.Length == 0 || member.Length == 0) return;
        _binding = controller.context.BindProperty(key, target, member);
    }
}
