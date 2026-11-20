namespace BEngine.Hosting;

/// <summary>Core startup presentation state; platform surfaces only draw and acknowledge it.</summary>
public interface IHostSplashProgress
{
    float LogoOpacity { get; }
    float Progress { get; }
    bool ShowProgressBar { get; }
    void RestartPresentation();
    void ConfirmProgressPresented(float displayedProgress);
}
