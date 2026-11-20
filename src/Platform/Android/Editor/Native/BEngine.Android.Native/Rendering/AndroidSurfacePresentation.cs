using System;
using Android.App;
using Android.Opengl;
using Android.Views;

namespace BEngine.Platform.AndroidNative;

/// <summary>Applies Android surface dimensions, refresh requests and immersive presentation.</summary>
public sealed class AndroidSurfacePresentation(Activity activity, GLSurfaceView surface)
{
    private readonly Activity _activity = activity;
    private readonly GLSurfaceView _surfaceView = surface;
    private int _requestedFrameRate, _fixedSurfaceWidth, _fixedSurfaceHeight;

    public void RestoreImmersiveMode()
    {
#pragma warning disable CA1422 // Legacy immersive flags are required on API 26+.
        if (_activity.Window?.DecorView is { } decor)
            decor.SystemUiFlags = SystemUiFlags.ImmersiveSticky | SystemUiFlags.Fullscreen |
                SystemUiFlags.HideNavigation | SystemUiFlags.LayoutStable |
                SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutFullscreen;
#pragma warning restore CA1422
    }
    public void ApplyFrameRate(bool force = false)
    {
        var rate = Math.Clamp(Application.targetFrameRate > 0 ? Application.targetFrameRate : 60, 1, 240);
        if (!force && rate == _requestedFrameRate) return;
        _requestedFrameRate = rate;
        // Games may adapt frame cadence; FIXED_SOURCE is intended for video.
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            _surfaceView?.Holder?.Surface?.SetFrameRate(rate, 0);
        _activity.RunOnUiThread(() =>
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(35) && _surfaceView is not null)
                _surfaceView.RequestedFrameRate = rate;
            if (_activity.Window?.Attributes is not { } attributes) return;
            attributes.PreferredRefreshRate = rate;
            _activity.Window.Attributes = attributes;
        });
    }

    public void UpdateSurfaceSize()
    {
        if (_surfaceView is not { Width: > 0, Height: > 0 } surface) return;
        var maximum = AndroidRuntimeSettings.maximumSurfaceDimension;
        var scale = maximum > 0 ? Math.Min(1d, (double)maximum / Math.Max(surface.Width, surface.Height)) : 1d;
        var targetWidth = Math.Max(1, (int)Math.Round(surface.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(surface.Height * scale));
        // LayoutChange can fire repeatedly during safe-area/insets updates.
        // Reissuing SetFixedSize for the same dimensions recreates the EGL
        // buffer queue on several phones and causes visible hitching or a
        // black frame. Resize only when the requested size actually changes.
        if (targetWidth == _fixedSurfaceWidth && targetHeight == _fixedSurfaceHeight) return;
        _fixedSurfaceWidth = targetWidth;
        _fixedSurfaceHeight = targetHeight;
        if (targetWidth == surface.Width && targetHeight == surface.Height) return;
        surface.Holder?.SetFixedSize(targetWidth, targetHeight);
    }

}
