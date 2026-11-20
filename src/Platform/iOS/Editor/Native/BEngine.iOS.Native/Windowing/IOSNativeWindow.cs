using BEngine.Rendering.Rhi;
using UIKit;

namespace BEngine.Platform.IosNative;

public sealed class IOSNativeWindow : IDisposable
{
    private readonly IOSInputView _view;
    private readonly IOSInputViewController _controller;
    private readonly IOSTextInput _textInput;
    private readonly IOSAccessibilityAdapter _accessibility;
    private bool _disposed;

    public UIWindow Window { get; }
    public UIView View => _view;

    public static void InitializePlatform()
    {
        iOSPlatform.Initialize();
        PlatformVideoDecoding.OpenVideo = (source, bytes, hardware) => new IOSVideoDecoder(source, bytes, hardware);
        PlatformVideoDecoding.OpenAudio = (source, bytes) => new IOSVideoAudioDecoder(source, bytes);
    }

    public IOSNativeWindow()
    {
        IOSAudioSession.Configure();
#pragma warning disable CA1422
        Window = new UIWindow(UIScreen.MainScreen.Bounds);
#pragma warning restore CA1422
        _view = new IOSInputView(Window.Bounds)
        {
            BackgroundColor = UIColor.Black,
            MultipleTouchEnabled = true,
            ContentScaleFactor = UIScreen.MainScreen.Scale
        };
        _controller = new IOSInputViewController(_view);
        Window.RootViewController = _controller;
        Window.MakeKeyAndVisible();
        _textInput = new IOSTextInput(_view);
        _accessibility = new IOSAccessibilityAdapter(_view);
        PlatformAccessibility.adapter = _accessibility;
    }

    public IGraphicsPresentationDevice CreateGraphicsDevice()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var (width, height) = GetDisplaySize();
        return IOSMetalGraphicsDevice.Create(_view.Handle, width, height);
    }

    public void PollInput() => IOSGamepadInput.Poll();

    public void SyncInput() => _textInput.Sync();

    public (int Width, int Height) ReportDisplay()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var (width, height) = GetDisplaySize();
        var scale = Math.Max(1, _view.ContentScaleFactor);
        DisplayUtility.Current.ReportDisplay(width, height, FullScreenMode.FullScreenWindow);
        var insets = _view.SafeAreaInsets;
        DisplayUtility.Current.ReportSafeArea(new((Fix64)(insets.Left * scale), (Fix64)(insets.Bottom * scale),
            (Fix64)(insets.Right * scale), (Fix64)(insets.Top * scale)));
        return (width, height);
    }

    private (int Width, int Height) GetDisplaySize()
    {
        var scale = Math.Max(1, _view.ContentScaleFactor);
        return (Math.Max(1, (int)Math.Ceiling(_view.Bounds.Width * scale)),
            Math.Max(1, (int)Math.Ceiling(_view.Bounds.Height * scale)));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (ReferenceEquals(PlatformAccessibility.adapter, _accessibility)) PlatformAccessibility.adapter = null;
        _view.Dispose();
        _controller.Dispose();
        Window.Dispose();
    }
}
