namespace BEngine.Platform;

public sealed class TouchInputUtility(ITouchInputService implementation)
{
    private readonly ITouchInputService _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));
    public static TouchInputUtility Current => RuntimeServices.GetRequiredService<TouchInputUtility>();
    public ITouchInputSource? Attach(IntPtr window, bool suppressEmulatedMouse = true) =>
        _implementation.Attach(window, suppressEmulatedMouse);
}
