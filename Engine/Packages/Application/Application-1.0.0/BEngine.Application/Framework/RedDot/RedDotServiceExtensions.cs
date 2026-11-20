using System;

namespace BEngine.App.RedDot;

public static class RedDotServiceExtensions
{
    /// <summary>Binds a scene indicator's active state to a count. Track the returned subscription in the view's
    /// lifetime; disposal releases the binding without changing the indicator's last active state.</summary>
    public static IDisposable BindActive(this IRedDotService service, string path, GameObject indicator)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(indicator);
        ObjectDisposedException.ThrowIf(indicator.isDestroyed, indicator);
        return service.Subscribe(path, change =>
        {
            if (!indicator.isDestroyed) indicator.SetActive(change.Count > 0);
        });
    }
}
