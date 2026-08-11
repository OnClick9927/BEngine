using BEngine.Serialization;

namespace BEngine.Animation;

public enum AnimatorControllerParameterType { Float, Int, Bool, Trigger }
public enum AnimatorConditionMode { If, IfNot, Greater, Less, Equals, NotEqual }

public sealed class AnimatorControllerParameter
{
    public string name { get; set; } = string.Empty;
    public AnimatorControllerParameterType type { get; set; }
    public Fix64 defaultFloat { get; set; }
    public int defaultInt { get; set; }
    public bool defaultBool { get; set; }
}

public sealed class AnimatorCondition
{
    public string parameter { get; set; } = string.Empty;
    public AnimatorConditionMode mode { get; set; } = AnimatorConditionMode.If;
    public Fix64 threshold { get; set; }
}

public sealed class AnimatorTransition
{
    public string destinationState { get; set; } = string.Empty;
    public bool hasExitTime { get; set; }
    public Fix64 exitTime { get; set; } = Fix64.One;
    public Fix64 duration { get; set; } = Fix64.Parse("0.25");
    public List<AnimatorCondition> conditions { get; set; } = [];
}

public sealed class AnimatorState
{
    public string name { get; set; } = "New State";
    public string clipPath { get; set; } = string.Empty;
    [HideInInspector]
    public AnimationClip? clip { get; set; }
    public Fix64 speed { get; set; } = Fix64.One;
    public bool loop { get; set; } = true;
    public List<AnimatorTransition> transitions { get; set; } = [];
    internal AnimationClip? ResolveClip() => clip ?? (!string.IsNullOrWhiteSpace(clipPath) && File.Exists(AssetPath.Resolve(clipPath)) ? AnimationClip.Load(AssetPath.Resolve(clipPath)) : null);
}

[CreateAssetMenu(fileName = "New Animator Controller", menuName = "Animation/Animator Controller", order = 111)]
public sealed class AnimatorController : BObject
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
    public void Save(string path) => YamlUtility.Save(AnimationAssetSerializer.ToDocument(this), path);
    public static AnimatorController Load(string path) => AnimationAssetSerializer.LoadController(path);
}

internal static class AssetPath
{
    public static string Resolve(string path) => Path.IsPathRooted(path) ? path :
        Path.GetFullPath(Path.Combine(Application.dataPath, path.Replace('/', Path.DirectorySeparatorChar)));
}
