using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BEngine;

internal static class ParticlePackageRegistration
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Prepare measured cold particle bodies when the package loads, without running a simulation.")]
    internal static void PrepareMethods() => ParticleSystem2D.RuntimeMethodPreparation.Prepare();
}

public sealed partial class ParticleSystem2D
{
    // This nested type has no ParticleSystem2D receiver and does not touch the
    // outer type's DefaultMaterial, particles, random seed or simulation clock.
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
            // Exact open delegates preserve named signatures without either
            // creating a component or depending on expression node subtypes
            // that are only reached by the hot module after Full stripping.
            Prepare<Func<ParticleSystem2D, Fix64, Vector2>>(nameof(RandomInsideCircle), typeof(Fix64));
            Prepare<Func<ParticleSystem2D, Fix64, Fix64, Fix64>>(nameof(NextRange), typeof(Fix64), typeof(Fix64));
            Prepare<Action<ParticleSystem2D, Particle2D>>(nameof(RecordTrail), typeof(Particle2D));
            Prepare<Action<ParticleSystem2D, ParticleSubEmitterEvent2D, Particle2D>>(nameof(EmitChildren), typeof(ParticleSubEmitterEvent2D), typeof(Particle2D));
            Prepare<Func<ParticleSystem2D, Particle2D, Fix64, Particle2D>>(nameof(StepParticle), typeof(Particle2D), typeof(Fix64));
            RuntimeHelpers.PrepareMethod(((Func<Color, Fix64, Color>)WithOpacity).Method.MethodHandle);
            RuntimeHelpers.PrepareMethod(((Func<Vector2, Vector2>)Abs).Method.MethodHandle);
            // Removing the first expired particles reaches a distinct closed
            // CoreLib body; preparing StepParticle does not prepare its callees.
            var removeRange = typeof(List<Particle2D>).GetMethod(nameof(List<Particle2D>.RemoveRange), [typeof(int), typeof(int)]);
            if (removeRange is not null) RuntimeHelpers.PrepareMethod(removeRange.MethodHandle);
            var grow = typeof(List<Particle2D>).GetMethod("AddWithResize", BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, types: [typeof(Particle2D)], modifiers: null);
            if (grow is not null) RuntimeHelpers.PrepareMethod(grow.MethodHandle);
            var resize = typeof(Array).GetMethod(nameof(Array.Resize), BindingFlags.Public | BindingFlags.Static);
            if (resize is not null) RuntimeHelpers.PrepareMethod(resize.MakeGenericMethod(typeof(Particle2D)).MethodHandle);
        }

        private static void Prepare<TDelegate>(string name, params Type[] parameters) where TDelegate : Delegate
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var method = typeof(ParticleSystem2D).GetMethod(name, flags, binder: null, types: parameters, modifiers: null);
            // Partial releases need not retain bodies used only by other modules.
            // Preparation is an optimization, not an additional preservation root.
            if (method is null) return;
            RuntimeHelpers.PrepareMethod(method.CreateDelegate<TDelegate>().Method.MethodHandle);
        }
    }
}
