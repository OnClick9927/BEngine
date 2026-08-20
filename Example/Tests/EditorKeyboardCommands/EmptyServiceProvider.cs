namespace BEngine.ExampleTests.EditorKeyboardCommands;

internal sealed class EmptyServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType) => null;
}
