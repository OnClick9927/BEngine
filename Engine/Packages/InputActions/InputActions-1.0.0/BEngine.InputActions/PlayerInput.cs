namespace BEngine.InputActions;

[AddComponentMenu("Input/Player Input")]
[DisallowMultipleComponent]
public sealed class PlayerInput : MonoBehaviour
{
    public InputActionAsset? actions { get; set; }
    public string defaultMap { get; set; } = "Gameplay";
    public string controlScheme { get; set; } = string.Empty;
    public int gamepadIndex { get; set; }
    public bool useKeyboard { get; set; } = true;
    public bool useTouch { get; set; } = true;
    public bool exclusiveGamepad { get; set; }
    public bool pairFirstAvailable { get; set; }
    [HideInInspector, YamlDotNet.Serialization.YamlIgnore]
    public InputActionSession? session { get; private set; }
    public override void OnEnable()
    {
        OnDisable();
        if (actions is null) return;
        var candidate = new InputActionSession(actions) { scheme = controlScheme, useKeyboard = useKeyboard, useTouch = useTouch };
        try
        {
            if (pairFirstAvailable)
            {
                candidate.UnpairGamepad(); candidate.TryPairNextAvailableGamepad(exclusiveGamepad);
            }
            else candidate.PairGamepad(gamepadIndex, exclusiveGamepad);
            candidate.EnableMap(defaultMap); session = candidate;
        }
        catch { candidate.Dispose(); throw; }
    }
    public override void OnDisable() { session?.Dispose(); session = null; }
    public override void OnDestroy() => OnDisable();
    internal void UpdateInput()
    {
        if (session is null) return;
        if (pairFirstAvailable && session.gamepadIndex < 0) session.TryPairNextAvailableGamepad(exclusiveGamepad);
        session.Update();
    }
}

public sealed class InputActionsRuntimeSystem : ISceneRuntimeSystem
{
    private readonly List<PlayerInput> _players = [];
    public string packageId => "com.bengine.input-actions";
    public int order => -200;
    public void BeforeUpdate(Scene scene, Fix64 deltaTime)
    {
        scene.GetComponents(_players);
        foreach (var player in _players)
            if (player.enabled && player.gameObject.activeInHierarchy) player.UpdateInput();
    }
}
