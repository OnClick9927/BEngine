namespace BEngine.Testing;

public enum TestMode
{
    Edit,
    Play,
    Player
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute(TestMode mode = TestMode.Edit) : Attribute
{
    public TestMode Mode { get; } = mode;
    public string Category { get; set; } = string.Empty;
    public int TimeoutMilliseconds { get; set; } = 10_000;
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class SetUpAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
public sealed class TearDownAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
public sealed class OneTimeSetUpAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
public sealed class OneTimeTearDownAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class IgnoreAttribute(string reason = "Ignored") : Attribute
{
    public string Reason { get; } = reason;
}

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct |
                AttributeTargets.Method | AttributeTargets.Constructor)]
public sealed class CoverageAttribute : Attribute;
