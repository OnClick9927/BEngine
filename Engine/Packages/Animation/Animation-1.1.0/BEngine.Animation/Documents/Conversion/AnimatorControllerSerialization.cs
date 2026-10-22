using BEngine.Serialization;

namespace BEngine;

internal static class AnimatorControllerSerialization
{
    internal static AnimatorController Restore(AnimatorControllerData document)
    {
        Validate(document);
        var controller = new AnimatorController
        {
            name = document.Name, defaultState = document.DefaultState,
            entryPosition = Position(document.EntryX, document.EntryY),
            anyStatePosition = Position(document.AnyX, document.AnyY),
            exitPosition = Position(document.ExitX, document.ExitY),
            parameters = [.. document.Parameters.Select(item => new AnimatorControllerParameter
            { name = item.Name, type = item.Type, defaultFloat = Fix64.FromRaw(item.DefaultFloat), defaultInt = item.DefaultInt, defaultBool = item.DefaultBool })],
            states = [.. document.States.Select(RestoreState)],
            anyStateTransitions = [.. document.AnyStateTransitions.Select(RestoreTransition)],
            entryTransitions = [.. document.EntryTransitions.Select(RestoreTransition)],
            layers = [.. document.Layers.Select(layer => new AnimatorControllerLayer
            {
                name = layer.Name, defaultWeight = Fix64.FromRaw(layer.Weight), defaultState = layer.StateMachine.DefaultState,
                entryPosition = Position(layer.StateMachine.EntryX, layer.StateMachine.EntryY),
                anyStatePosition = Position(layer.StateMachine.AnyX, layer.StateMachine.AnyY),
                exitPosition = Position(layer.StateMachine.ExitX, layer.StateMachine.ExitY),
                states = [.. layer.StateMachine.States.Select(RestoreState)],
                anyStateTransitions = [.. layer.StateMachine.AnyStateTransitions.Select(RestoreTransition)],
                entryTransitions = [.. layer.StateMachine.EntryTransitions.Select(RestoreTransition)]
            })]
        };
        if (document.Version == 2)
        {
            if (document.Graph is null) throw new InvalidDataException("Missing Animator controller graph.");
            ComponentObjectGraphSerializer.Restore(controller, document.Graph);
        }
        ValidateTrees(controller);
        return controller;
    }
    private static Vector2 Position(long x, long y) => new(Fix64.FromRaw(x), Fix64.FromRaw(y));
    private static AnimatorState RestoreState(AnimatorStateData state) => new()
    {
        name = state.Name, clipPath = state.ClipPath,
        blendTree = state.BlendTree is { } tree ? new AnimatorBlendTree
        {
            parameter = tree.Parameter,
            motions = [.. tree.Motions.Select(item => new AnimatorBlendMotion
            { clipPath = item.ClipPath, threshold = Fix64.FromRaw(item.Threshold) })]
        } : null,
        speed = Fix64.FromRaw(state.Speed), loop = state.Loop,
        editorPosition = state.EditorX is { } x && state.EditorY is { } y ? Position(x, y) : null,
        transitions = [.. state.Transitions.Select(RestoreTransition)]
    };
    private static AnimatorTransition RestoreTransition(AnimatorTransitionData item) => new()
    {
        destinationState = item.DestinationState, isExit = item.IsExit, hasFixedDuration = item.HasFixedDuration,
        offset = Fix64.FromRaw(item.Offset), canTransitionToSelf = item.CanTransitionToSelf,
        hasExitTime = item.HasExitTime, exitTime = Fix64.FromRaw(item.ExitTime), duration = Fix64.FromRaw(item.Duration),
        conditions = [.. item.Conditions.Select(condition => new AnimatorCondition
        { parameter = condition.Parameter, mode = condition.Mode, threshold = Fix64.FromRaw(condition.Threshold) })]
    };
    private static void ValidateTrees(AnimatorController controller)
    {
        controller.ValidateHierarchy();
        for (var index = 0; index < controller.layers.Count; index++)
        {
            var layer = controller.layers[index];
            if (!Enum.IsDefined(layer.blendingMode) || layer.syncedLayerIndex < -1 || layer.syncedLayerIndex > index)
                throw new InvalidDataException("Invalid Animator layer blending mode or synchronization source.");
        }
        foreach (var state in Enumerable.Range(0, controller.layerCount).SelectMany(controller.GetAllStates))
            state.blendTree?.Validate();
    }
    internal static AnimatorControllerData Capture(AnimatorController controller)
    {
        ValidateTrees(controller);
        return new() { Name = controller.name, Graph = ComponentObjectGraphSerializer.Capture(controller) };
    }

    internal static void Validate(AnimatorControllerData document)
    {
        if (document.Format != "BEngine.AnimatorController" || document.Version is not (1 or 2))
            throw new InvalidDataException("Unsupported animator controller.");
        if (document.Layers.Any(layer => layer.StateMachine.Layers.Count != 0))
            throw new InvalidDataException("Animator layers cannot contain nested layers.");
    }
    internal static AnimatorController Load(string path) => Document<AnimatorController>
        .Read(path, sourcePath => Restore(YamlUtility.Load<AnimatorControllerData>(sourcePath))).ToAsset();
    internal static void Save(AnimatorController controller, string path) =>
        Document<AnimatorController>.FromAsset(controller)
            .Write(path, static (asset, destination) => YamlUtility.Save(Capture(asset), destination));
}
