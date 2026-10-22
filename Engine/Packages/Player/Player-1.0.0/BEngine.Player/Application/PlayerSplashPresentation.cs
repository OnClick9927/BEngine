namespace BEngine.Player;

public sealed record PlayerSplashPresentation(
    string ProductName,
    int ImageWidth,
    int ImageHeight,
    byte[] BgraPixels,
    uint BackgroundColor);
