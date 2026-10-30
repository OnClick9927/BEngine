using System;
using Android.Content;
using Android.Graphics;
using Android.Views;
using BEngine.Player;

namespace BEngine.Player.Hosts.Android;

/// <summary>UI-thread presentation remains animated while the GL thread prepares the Player.</summary>
internal sealed class AndroidPlayerStartupView : View
{
    private readonly Bitmap? _logo;
    private readonly Paint _paint = new(PaintFlags.AntiAlias | PaintFlags.FilterBitmap);
    private readonly global::Android.Graphics.Color _background;

    public AndroidPlayerStartupView(Context context) : base(context)
    {
        var bytes = PlayerStartupEnvironment.LogoPng;
        _logo = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
        _background = global::Android.Graphics.Color.ParseColor(PlayerStartupEnvironment.BackgroundColor[..7]);
        SetWillNotDraw(false);
        Clickable = true;
        ImportantForAccessibility = ImportantForAccessibility.No;
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        canvas.DrawColor(_background);
        if (_logo is not { } logo || PlayerStartupEnvironment.Current is not { } progress) return;
        var density = Resources?.DisplayMetrics?.Density ?? 1;
        var scale = Math.Min(Math.Min(240 * density, Width * .48f) / logo.Width,
            Math.Min(180 * density, Height * .32f) / logo.Height);
        var w = logo.Width * scale; var h = logo.Height * scale;
        var x = (Width - w) / 2; var y = (Height - h) / 2;
        _paint.Alpha = (int)(255 * progress.LogoOpacity);
        canvas.DrawBitmap(logo, null, new RectF(x, y, x + w, y + h), _paint);
        if (progress.ShowProgressBar)
        {
            var displayedProgress = progress.Progress;
            var width = Math.Min(240 * density, Width * .48f);
            var left = (Width - width) / 2; var top = y + h + 28 * density;
            _paint.Alpha = 255;
            _paint.Color = global::Android.Graphics.Color.Rgb(57, 65, 73);
            canvas.DrawRect(left, top, left + width, top + 5 * density, _paint);
            _paint.Color = global::Android.Graphics.Color.Rgb(199, 211, 223);
            canvas.DrawRect(left, top, left + width * displayedProgress, top + 5 * density, _paint);
            progress.ConfirmProgressPresented(displayedProgress);
        }
        PostInvalidateOnAnimation();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _logo?.Dispose(); _paint.Dispose(); }
        base.Dispose(disposing);
    }
}
