using BEngine.Documents;

namespace BEngine.Animation;

internal static class AnimatorControllerSerialization
{
    internal static AnimatorController Restore(AnimatorControllerData document)
    {
        Validate(document);
        return new AnimatorController
        {
            name = document.Name,
            defaultState = document.DefaultState,
            parameters = [.. document.Parameters.Select(item => new AnimatorControllerParameter
            {
                name = item.Name,
                type = item.Type,
                defaultFloat = Fix64.FromRaw(item.DefaultFloat),
                defaultInt = item.DefaultInt,
                defaultBool = item.DefaultBool
            })],
            states = [.. document.States.Select(state => new AnimatorState
            {
                name = state.Name,
                clipPath = state.ClipPath,
                speed = Fix64.FromRaw(state.Speed),
                loop = state.Loop,
                transitions = [.. state.Transitions.Select(transition => new AnimatorTransition
                {
                    destinationState = transition.DestinationState,
                    hasExitTime = transition.HasExitTime,
                    exitTime = Fix64.FromRaw(transition.ExitTime),
                    duration = Fix64.FromRaw(transition.Duration),
                    conditions = [.. transition.Conditions.Select(condition => new AnimatorCondition
                    {
                        parameter = condition.Parameter,
                        mode = condition.Mode,
                        threshold = Fix64.FromRaw(condition.Threshold)
                    })]
                })]
            })]
        };
    }

    internal static AnimatorControllerData Capture(AnimatorController controller) => new()
    {
        Name = controller.name,
        DefaultState = controller.defaultState,
        Parameters = [.. controller.parameters.Select(item => new AnimatorParameterData
        {
            Name = item.name,
            Type = item.type,
            DefaultFloat = item.defaultFloat.RawValue,
            DefaultInt = item.defaultInt,
            DefaultBool = item.defaultBool
        })],
        States = [.. controller.states.Select(state => new AnimatorStateData
        {
            Name = state.name,
            ClipPath = state.clipPath,
            Speed = state.speed.RawValue,
            Loop = state.loop,
            Transitions = [.. state.transitions.Select(transition => new AnimatorTransitionData
            {
                DestinationState = transition.destinationState,
                HasExitTime = transition.hasExitTime,
                ExitTime = transition.exitTime.RawValue,
                Duration = transition.duration.RawValue,
                Conditions = [.. transition.conditions.Select(condition => new AnimatorConditionData
                {
                    Parameter = condition.parameter,
                    Mode = condition.mode,
                    Threshold = condition.threshold.RawValue
                })]
            })]
        })]
    };

    internal static void Validate(AnimatorControllerData document)
    {
        if (document.Format != "BEngine.AnimatorController" || document.Version != 1)
            throw new InvalidDataException("Unsupported animator controller.");
    }

    internal static AnimatorController Load(string path) => Document<AnimatorController>
        .Read(path, sourcePath => Restore(YamlUtility.Load<AnimatorControllerData>(sourcePath))).ToAsset();

    internal static void Save(AnimatorController controller, string path) =>
        Document<AnimatorController>.FromAsset(controller)
            .Write(path, static (asset, destination) => YamlUtility.Save(Capture(asset), destination));
}
