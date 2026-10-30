using System;
using BEngine.Player;
using CoreGraphics;
using Foundation;
using UIKit;

namespace BEngine.Player.Hosts.iOS;

internal sealed class IOSPlayerStartupView : UIView
{
    private readonly UIImageView _logo;
    private readonly UIProgressView _progress;
    private readonly NSTimer _timer;

    internal IOSPlayerStartupView(CGRect frame) : base(frame)
    {
        var color = Convert.ToUInt32(PlayerStartupEnvironment.BackgroundColor.Substring(1, 6), 16);
        BackgroundColor = UIColor.FromRGB((byte)(color >> 16), (byte)(color >> 8), (byte)color);
        using var data = NSData.FromArray(PlayerStartupEnvironment.LogoPng);
        _logo = new UIImageView(UIImage.LoadFromData(data)) { ContentMode = UIViewContentMode.ScaleAspectFit, Alpha = 0 };
        _progress = new UIProgressView(UIProgressViewStyle.Default)
        {
            TrackTintColor = UIColor.FromRGB(57, 65, 73),
            ProgressTintColor = UIColor.FromRGB(199, 211, 223), Hidden = true
        };
        AddSubview(_logo); AddSubview(_progress);
        AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
        _timer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromSeconds(1d / 60), _ =>
        {
            if (PlayerStartupEnvironment.Current is not { } state) return;
            // The preceding UI tick has had an opportunity to present this value.
            if (!_progress.Hidden) state.ConfirmProgressPresented(_progress.Progress);
            _progress.Hidden = !state.ShowProgressBar;
            _progress.Progress = state.Progress;
        });
    }

    internal void AnimateLogo() => UIView.Animate(.5, 0, UIViewAnimationOptions.CurveLinear, () => _logo.Alpha = 1, () => { });

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        var width = Math.Min(240, Bounds.Width * .48); var height = Math.Min(180, Bounds.Height * .32);
        _logo.Frame = new CGRect((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);
        _progress.Frame = new CGRect((Bounds.Width - width) / 2, _logo.Frame.Bottom + 28, width, 5);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Invalidate(); _timer.Dispose(); _logo.Image?.Dispose(); _logo.Dispose(); _progress.Dispose(); }
        base.Dispose(disposing);
    }
}
