using BEngine.Build;
using BEngine.Content;
using BEngine.Platform;
using BEngine.Rendering;

namespace BEngine.Player;

/// <summary>Owns the platform splash window shown before the project's AOT startup flow.</summary>
public sealed class PlayerSplashScreen : IDisposable
{
    private readonly IPlayerSplashWindow? _window;
    private readonly PlatformUtility _platformService;
    private readonly TimeSpan _minimumDuration;
    private readonly long _shownTimestamp;
    private bool _closed;

    private PlayerSplashScreen(
        IPlayerSplashWindow? window,
        TimeSpan minimumDuration,
        PlatformUtility platformService)
    {
        _window = window;
        _platformService = platformService;
        _minimumDuration = minimumDuration;
        _shownTimestamp = platformService.MonotonicTimestamp;
    }

    public bool IsVisible => !_closed && _window?.IsVisible == true;

    public void Pump()
    {
        if (!_closed) _window?.Pump();
    }

    public static PlayerSplashScreen Show(
        PlayerBootstrapManifest? manifest,
        string playerDirectory,
        IPlayerSplashHost? host = null,
        FileUtility? fileSystem = null,
        PathUtility? pathService = null,
        PlatformUtility? platformService = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerDirectory);
        fileSystem ??= RuntimeServiceAccess.FileSystem;
        pathService ??= RuntimeServiceAccess.PathService;
        platformService ??= RuntimeServiceAccess.PlatformService;
        if (manifest is null || !manifest.SplashScreenEnabled)
            return new PlayerSplashScreen(null, TimeSpan.Zero, platformService);
        manifest.Validate();
        var minimumDuration = TimeSpan.FromSeconds(manifest.SplashMinimumDurationSeconds);
        if (manifest.Version >= 4)
        {
            var archive = BuiltInResourceArchive.Open(
                manifest.ResolvePlayerResourceArchive(playerDirectory), fileSystem, pathService);
            return Show(
                archive.ReadBytes(manifest.SplashImageResource),
                manifest.ProductName,
                manifest.SplashBackgroundColor,
                minimumDuration,
                host,
                platformService);
        }
        var imagePath = manifest.ResolveSplashImage(playerDirectory) ??
                        throw new InvalidDataException("The enabled Player splash screen has no image path.");
        return Show(
            imagePath,
            manifest.ProductName,
            manifest.SplashBackgroundColor,
            minimumDuration,
            host,
            fileSystem,
            pathService,
            platformService);
    }

    public static PlayerSplashScreen Show(
        string imagePath,
        string productName,
        string backgroundColor = "#10161A",
        TimeSpan minimumDuration = default,
        IPlayerSplashHost? host = null,
        FileUtility? fileSystem = null,
        PathUtility? pathService = null,
        PlatformUtility? platformService = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ValidateMinimumDuration(minimumDuration);
        fileSystem ??= RuntimeServiceAccess.FileSystem;
        pathService ??= RuntimeServiceAccess.PathService;
        platformService ??= RuntimeServiceAccess.PlatformService;
        var fullPath = pathService.GetFullPath(imagePath);
        if (!fileSystem.FileExists(fullPath))
            throw new FileNotFoundException("The Player splash image was not found.", fullPath);
        if (fileSystem.GetFileLength(fullPath) > 64 * 1024 * 1024)
            throw new InvalidDataException("The Player splash image exceeds 64 MiB.");
        return Show(
            fileSystem.ReadAllBytes(fullPath), productName, backgroundColor, minimumDuration, host,
            platformService);
    }

    public static PlayerSplashScreen Show(
        ReadOnlySpan<byte> imageBytes,
        string productName,
        string backgroundColor = "#10161A",
        TimeSpan minimumDuration = default,
        IPlayerSplashHost? host = null,
        PlatformUtility? platformService = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ValidateMinimumDuration(minimumDuration);
        platformService ??= RuntimeServiceAccess.PlatformService;
        if (imageBytes.Length > 64 * 1024 * 1024)
            throw new InvalidDataException("The Player splash image exceeds 64 MiB.");
        if (!PngImageCodec.TryDecode(imageBytes,
                out var width, out var height, out var rgba) || width > 8192 || height > 8192)
            throw new InvalidDataException(
                "The Player splash image is not a supported PNG or exceeds 8192x8192.");

        var color = ParseColor(backgroundColor);
        var window = host?.Show(new PlayerSplashPresentation(
            productName,
            width,
            height,
            CompositeToBgra(rgba, color),
            color.ColorRef));
        return new PlayerSplashScreen(window, minimumDuration, platformService);
    }

    public void WaitForMinimumDuration(BCancellationToken cancellationToken = default) =>
        PlayerMainThread.Complete(WaitForMinimumDurationAsync(cancellationToken));

    public async BValueTask WaitForMinimumDurationAsync(BCancellationToken cancellationToken = default)
    {
        if (_minimumDuration <= TimeSpan.Zero) return;
        var remaining = PlayerStartupEnvironment.Current is { } progress
            ? TimeSpan.FromSeconds(Math.Max(0, PlayerStartupProgress.LogoFadeSeconds - progress.ElapsedSeconds))
            : _minimumDuration - _platformService.GetElapsedTime(_shownTimestamp);
        if (remaining > TimeSpan.Zero) await BTask.Delay(remaining, cancellationToken);
        while (PlayerStartupEnvironment.Current is { CanContinue: false })
            await BTask.Delay(8, cancellationToken);
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        _window?.Dispose();
    }

    public void CloseAfterMinimumDuration(BCancellationToken cancellationToken = default)
    {
        WaitForMinimumDuration(cancellationToken);
        Close();
    }

    public async BValueTask CloseAfterMinimumDurationAsync(
        BCancellationToken cancellationToken = default)
    {
        await WaitForMinimumDurationAsync(cancellationToken);
        Close();
    }

    public void Dispose() => Close();

    private static void ValidateMinimumDuration(TimeSpan minimumDuration)
    {
        if (minimumDuration < TimeSpan.Zero || minimumDuration > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(minimumDuration));
    }

    private static SplashColor ParseColor(string value)
    {
        if (value is null || value.Length is not (7 or 9) || value[0] != '#' ||
            !uint.TryParse(value.AsSpan(1), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var packed))
            throw new InvalidDataException("Splash background color must use #RRGGBB or #RRGGBBAA format.");
        if (value.Length == 7) packed = (packed << 8) | 0xff;
        return new SplashColor(
            (byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
    }

    private static byte[] CompositeToBgra(byte[] rgba, SplashColor background)
    {
        var bgra = new byte[rgba.Length];
        for (var index = 0; index < rgba.Length; index += 4)
        {
            var alpha = rgba[index + 3];
            var inverse = 255 - alpha;
            bgra[index] = Blend(rgba[index + 2], background.Blue, alpha, inverse);
            bgra[index + 1] = Blend(rgba[index + 1], background.Green, alpha, inverse);
            bgra[index + 2] = Blend(rgba[index], background.Red, alpha, inverse);
            bgra[index + 3] = 255;
        }
        return bgra;
    }

    private static byte Blend(byte foreground, byte background, int alpha, int inverse) =>
        (byte)((foreground * alpha + background * inverse + 127) / 255);

    private readonly record struct SplashColor(byte Red, byte Green, byte Blue, byte Alpha)
    {
        internal uint ColorRef => (uint)(Red | Green << 8 | Blue << 16);
    }
}
