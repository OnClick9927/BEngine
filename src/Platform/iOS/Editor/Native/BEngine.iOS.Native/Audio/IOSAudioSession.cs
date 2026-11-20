using System;
using AVFoundation;
using Foundation;

namespace BEngine.Platform.IosNative;

public static class IOSAudioSession
{
    public static void Configure()
    {
        var session = AVAudioSession.SharedInstance();
        ThrowIfAudioSessionFailed(session.SetCategory(AVAudioSessionCategory.SoloAmbient), "set category");
        ThrowIfAudioSessionFailed(session.SetActive(true), "activate");
        Console.WriteLine("BENGINE_IOS_AUDIO_SESSION_OK|state=active");
    }

    public static void Activate()
    {
        try
        {
            ThrowIfAudioSessionFailed(AVAudioSession.SharedInstance().SetActive(true), "reactivate");
            BEngine.Platform.iOSPlatform.ResumeAudioOutputs();
            Console.WriteLine("BENGINE_IOS_AUDIO_SESSION_OK|state=active");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("BENGINE_IOS_AUDIO_SESSION_FAILED|state=active|" + exception);
        }
    }

    public static void Deactivate()
    {
        try
        {
            BEngine.Platform.iOSPlatform.SuspendAudioOutputs();
            ThrowIfAudioSessionFailed(AVAudioSession.SharedInstance().SetActive(false,
                AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation), "deactivate");
            Console.WriteLine("BENGINE_IOS_AUDIO_SESSION_OK|state=inactive");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("BENGINE_IOS_AUDIO_SESSION_FAILED|state=inactive|" + exception);
        }
    }

    private static void ThrowIfAudioSessionFailed(NSError? error, string operation)
    {
        if (error is null) return;
        throw new InvalidOperationException($"AVAudioSession could not {operation}: {error}.");
    }

}
