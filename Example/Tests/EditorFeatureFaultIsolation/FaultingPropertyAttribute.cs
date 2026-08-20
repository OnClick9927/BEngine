using BEngine;

namespace BEngine.ExampleTests.EditorFeatureFaultIsolation;

[AttributeUsage(AttributeTargets.Field)]
internal sealed class FaultingPropertyAttribute : PropertyAttribute;
