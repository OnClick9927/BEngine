using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BEngine;

public sealed partial class PlayableDirector
{
    // A separate nested type lets registration prepare instance bodies without
    // constructing a director or initializing its playback state.
    internal static class RuntimeMethodPreparation
    {
        internal static void Prepare()
        {
            if (!OperatingSystem.IsWindows() || !RuntimeFeature.IsDynamicCodeSupported ||
                !RuntimeFeature.IsDynamicCodeCompiled || RuntimeInformation.FrameworkDescription
                    .StartsWith("Mono", StringComparison.OrdinalIgnoreCase)) return;
            PrepareWindowsJitMethods();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PrepareWindowsJitMethods()
        {
            // Bind exact open instance signatures; no director/asset receiver
            // is constructed and no target delegate is invoked. Unlike an
            // expression tree, this has no dynamically selected BCL node
            // hierarchy that a Full Player can trim outside the hot module.
            var owner = typeof(PlayableDirector);
            // Preparing an instance body does not initialize the type's signal
            // comparers. Move that measured first-play initialization here too.
            RuntimeHelpers.RunClassConstructor(owner.TypeHandle);
            Prepare<Action<PlayableDirector>>(owner, nameof(Play));
            Prepare<Action<PlayableDirector>>(owner, nameof(ResetEvaluationState));
            Prepare<Action<PlayableDirector>>(owner, nameof(ReleaseControls));
            Prepare<Action<PlayableDirector>>(owner, nameof(RestoreCameraDefaults));
            Prepare<Action<PlayableDirector>>(owner, nameof(Evaluate));
            Prepare<Action<PlayableDirector>>(owner, nameof(EvaluateCore));
            Prepare<Action<PlayableDirector, TimelineAsset>>(owner, nameof(EnsurePoses), typeof(TimelineAsset));
            Prepare<Func<PlayableDirector, string, GameObject?>>(owner, nameof(GetGenericBinding), typeof(string));
            Prepare<Action<PlayableDirector, Dictionary<PlayableDirector, TimelineClip>>>(owner, nameof(EvaluateControls),
                typeof(Dictionary<PlayableDirector, TimelineClip>));
            Prepare<Func<TimelineAsset, Fix64>>(typeof(TimelineAsset), "get_" + nameof(TimelineAsset.duration));
            Prepare<Action<PlayableDirector, TimelineAsset, Fix64, Fix64, Fix64, bool, HashSet<PlayableDirector>>>(owner,
                nameof(DispatchSignalRange), typeof(TimelineAsset), typeof(Fix64), typeof(Fix64), typeof(Fix64), typeof(bool), typeof(HashSet<PlayableDirector>));
            Prepare<Action<PlayableDirector, TimelineAsset, Fix64, Fix64, Fix64, bool>>(owner,
                nameof(DispatchSignals), typeof(TimelineAsset), typeof(Fix64), typeof(Fix64), typeof(Fix64), typeof(bool));
            PrepareOptionalBody(owner, nameof(CompareSignalsAscending), typeof(SignalMarker), typeof(SignalMarker));
            PrepareOptionalBody(owner, nameof(CompareSignalsDescending), typeof(SignalMarker), typeof(SignalMarker));
            PrepareOptionalBody(typeof(List<SignalMarker>), "AddWithResize", typeof(SignalMarker));
            PrepareOptionalBody(typeof(List<SignalMarker>), nameof(List<SignalMarker>.Sort), typeof(Comparison<SignalMarker>));
            RuntimeHelpers.RunClassConstructor(typeof(List<SignalMarker>).TypeHandle);
            var callbacks = owner.GetNestedType("<>c", BindingFlags.NonPublic);
            if (callbacks is not null)
                foreach (var callback in callbacks.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (callback.Name.StartsWith("<DispatchSignalRange>b__", StringComparison.Ordinal) &&
                        callback.ReturnType == typeof(bool) && callback.GetParameters() is [{ ParameterType: var parameter }] &&
                        parameter == typeof(TimelineTrack))
                        RuntimeHelpers.PrepareMethod(callback.MethodHandle);
            var sortDefinition = typeof(List<>).Assembly.GetType("System.Collections.Generic.ArraySortHelper`1", throwOnError: false);
            if (sortDefinition is not null)
            {
                var sort = sortDefinition.MakeGenericType(typeof(SignalMarker));
                PrepareOptionalBody(sort, "Sort", typeof(Span<SignalMarker>), typeof(Comparison<SignalMarker>));
                PrepareOptionalBody(sort, "IntrospectiveSort", typeof(Span<SignalMarker>), typeof(Comparison<SignalMarker>));
                PrepareOptionalBody(sort, "IntroSort", typeof(Span<SignalMarker>), typeof(int), typeof(Comparison<SignalMarker>));
                PrepareOptionalBody(sort, "InsertionSort", typeof(Span<SignalMarker>), typeof(Comparison<SignalMarker>));
            }
        }

        private static void PrepareOptionalBody(Type owner, string name, params Type[] parameters)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var method = owner.GetMethod(name, flags, binder: null, types: parameters, modifiers: null);
            if (method is not null && !method.ContainsGenericParameters) RuntimeHelpers.PrepareMethod(method.MethodHandle);
        }

        private static void Prepare<TDelegate>(Type owner, string name, params Type[] parameters) where TDelegate : Delegate
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var method = owner.GetMethod(name, flags, binder: null, types: parameters, modifiers: null);
            // A loader-only release can legitimately omit unused playback bodies.
            // Optional compilation must not turn their removal into a load failure.
            if (method is null) return;
            RuntimeHelpers.PrepareMethod(method.CreateDelegate<TDelegate>().Method.MethodHandle);
        }
    }
}
