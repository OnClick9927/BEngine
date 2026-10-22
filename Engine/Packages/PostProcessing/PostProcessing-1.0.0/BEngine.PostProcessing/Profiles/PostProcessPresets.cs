namespace BEngine.PostProcessing;

/// <summary>Creates independent profiles that can be edited without changing a shared preset.</summary>
public static class PostProcessPresets
{
    public static PostProcessProfile Warm()
    {
        var profile = new PostProcessProfile { name = "Warm" };
        profile.colorAdjustments.colorFilter.value = new Color(1, Fix64.Parse("0.85"), Fix64.Parse("0.65"));
        profile.colorAdjustments.contrast.value = 15;
        profile.bloom.intensity.value = Fix64.Parse("0.3");
        return profile;
    }

    public static PostProcessProfile Noir()
    {
        var profile = new PostProcessProfile { name = "Noir" };
        profile.colorAdjustments.saturation.value = -100;
        profile.colorAdjustments.contrast.value = 30;
        profile.vignette.intensity.value = Fix64.Parse("0.45");
        return profile;
    }
}
