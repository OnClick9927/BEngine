using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.App.Value;

/// <summary>Uses the supplied provider's lifetimes and keyed registrations. Never owns that provider.</summary>
public sealed class ValueService : IValueService
{
    // Ephemeron keys let a collectible game assembly and its reflection plan die together.
    private static readonly ConditionalWeakTable<Type, InjectionPlan> Plans = new();
    private IServiceProvider? _provider;

    public ValueService(IServiceProvider provider) => _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    public object? Get(Type type, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        var provider = _provider ?? throw new ObjectDisposedException(nameof(ValueService));
        return string.IsNullOrEmpty(name) ? provider.GetService(type) : provider.GetKeyedService(type, name);
    }

    public object GetRequired(Type type, string? name = null) => Get(type, name) ??
        throw new InvalidOperationException($"No value of type '{type}' is registered{(string.IsNullOrEmpty(name) ? "." : $" for name '{name}'.")}");

    public bool TryGet(Type type, [NotNullWhen(true)] out object? value, string? name = null)
    {
        value = Get(type, name);
        return value is not null;
    }

    [RequiresUnreferencedCode("Member injection needs all attributed fields/properties, including private base members, preserved. Prefer constructor injection in trimmed players.")]
    public void Inject(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(_provider is null, this);
        var plan = Plans.GetValue(target.GetType(), static type => CreatePlan(type));
        foreach (var member in plan.Members)
        {
            var value = Get(member.ServiceType, member.Name) ?? throw new InvalidOperationException(
                $"Cannot inject '{member.Member.DeclaringType}.{member.Member.Name}': service '{member.ServiceType}'" +
                (member.Name is null ? " is not registered." : $" with name '{member.Name}' is not registered."));
            try
            {
                if (member.Member is FieldInfo field) field.SetValue(target, value);
                else ((PropertyInfo)member.Member).SetValue(target, value);
            }
            catch (Exception error) when (error is ArgumentException or TargetInvocationException or MemberAccessException)
            {
                throw new InvalidOperationException($"Failed to inject '{member.Member.DeclaringType}.{member.Member.Name}'.", error);
            }
        }
    }

    /// <summary>Releases only this facade's reference. Provider scopes and resolved instances remain caller-owned.</summary>
    public void Dispose() => _provider = null;

    [RequiresUnreferencedCode("Reads explicitly attributed members across the target's inheritance chain.")]
    private static InjectionPlan CreatePlan(Type type)
    {
        var hierarchy = new List<Type>();
        for (var current = type; current is not null; current = current.BaseType) hierarchy.Add(current);
        var members = new List<InjectionMember>();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                   BindingFlags.Static | BindingFlags.DeclaredOnly;
        for (var index = hierarchy.Count - 1; index >= 0; index--)
        {
            foreach (var field in hierarchy[index].GetFields(flags))
            {
                if (field.GetCustomAttribute<InjectAttribute>(false) is not { } inject) continue;
                if (field.IsStatic || field.IsInitOnly || field.IsLiteral)
                    throw InvalidMember(field, "must be a writable instance field");
                members.Add(new(field, field.FieldType, inject.Name));
            }
            foreach (var property in hierarchy[index].GetProperties(flags))
            {
                if (property.GetCustomAttribute<InjectAttribute>(false) is not { } inject) continue;
                var setter = property.GetSetMethod(true);
                if (setter is null || setter.IsStatic || property.GetIndexParameters().Length != 0 ||
                    Array.Exists(setter.ReturnParameter.GetRequiredCustomModifiers(),
                        static modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit"))
                    throw InvalidMember(property, "must be a writable instance property without index parameters or an init-only setter");
                // A virtual override is set once through its most-derived declaration.
                if (setter.IsVirtual) RemovePreviousOverride(members, setter);
                members.Add(new(property, property.PropertyType, inject.Name));
            }
        }
        return new(members.ToArray());
    }

    private static void RemovePreviousOverride(List<InjectionMember> members, MethodInfo setter)
    {
        var definition = setter.GetBaseDefinition();
        for (var index = 0; index < members.Count; index++)
        {
            if (members[index].Member is not PropertyInfo previous) continue;
            var previousSetter = previous.GetSetMethod(true)!;
            if (!previousSetter.IsVirtual || !previousSetter.GetBaseDefinition().Equals(definition)) continue;
            // Prefer the override's own attribute (including its named registration).
            members.RemoveAt(index);
            return;
        }
    }

    private static InvalidOperationException InvalidMember(MemberInfo member, string reason) =>
        new($"[Inject] member '{member.DeclaringType}.{member.Name}' {reason}.");

    private sealed record InjectionPlan(InjectionMember[] Members);
    private readonly record struct InjectionMember(MemberInfo Member, Type ServiceType, string? Name);
}
