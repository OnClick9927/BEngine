using System;
using Android.App;
using Android.Runtime;

[assembly: UsesPermission("android.permission.INTERNET")]

namespace BEngine.Player.Hosts.Android;

[Application(UsesCleartextTraffic = true, AppCategory = global::Android.Content.PM.ApplicationCategories.Game)]
internal sealed class BEngineAndroidApplication : global::Android.App.Application
{
    public BEngineAndroidApplication(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }
}
