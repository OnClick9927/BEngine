using BEngine.Documents;

namespace BEngine.Animation;

[CreateAssetMenu(fileName = "New Animator Controller", menuName = "Animation/Animator Controller", order = 111)]
[EditorIcon("Icons/Assets/AssetAnimation.png")]
public sealed class AnimatorController : ScriptableObject
{
    public string defaultState { get; set; } = string.Empty;
    public List<AnimatorControllerParameter> parameters { get; set; } = [];
    public List<AnimatorState> states { get; set; } = [];
    public AnimatorState? FindState(string stateName) => states.FirstOrDefault(item => item.name == stateName);
    public void AddState(AnimatorState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (states.Any(item => item.name == state.name)) throw new InvalidOperationException($"Animator state '{state.name}' already exists.");
        states.Add(state);
        if (string.IsNullOrWhiteSpace(defaultState)) defaultState = state.name;
    }
    public void Save(string path) => AnimatorControllerSerialization.Save(this, path);
    public static AnimatorController Load(string path) => AnimatorControllerSerialization.Load(path);
}
