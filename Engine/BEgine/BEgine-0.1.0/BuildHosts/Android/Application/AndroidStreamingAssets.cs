using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Android.Content.Res;
using BEngine.Platform;
using Microsoft.Win32.SafeHandles;

namespace BEngine.Player.Hosts.Android;

internal static class AndroidStreamingAssets
{
    internal static IFileSystem Mount(AssetManager assets, string apkPath, string dataDirectory, string root)
    {
        var assetPrefix = $"BEnginePayload/{dataDirectory}/StreamingAssets/";
        var zipPrefix = "assets/" + assetPrefix;
        using var apk = ZipFile.OpenRead(apkPath);
        var files = apk.Entries.Where(entry => entry.FullName.StartsWith(zipPrefix, StringComparison.Ordinal) &&
                                              !entry.FullName.EndsWith('/'))
            .ToDictionary(entry => entry.FullName[zipPrefix.Length..], entry => entry.Length, StringComparer.Ordinal);
        return new ReadOnlyFileSystemMount(PhysicalFileSystem.Shared, root, files, relative =>
        {
            var path = assetPrefix + relative;
            AssetFileDescriptor? descriptor = null;
            try
            {
                // ABs are stored without an outer APK compression layer. Their
                // own ZIP/encryption can seek directly within the APK's fd.
                descriptor = assets.OpenFd(path);
                var handle = new SafeFileHandle((IntPtr)descriptor.ParcelFileDescriptor!.Fd, ownsHandle: false);
                return new ReadOnlyFileSegmentStream(handle, descriptor.StartOffset, descriptor.Length,
                    new AssetOwner(descriptor));
            }
            catch (Java.IO.FileNotFoundException)
            {
                descriptor?.Close(); descriptor?.Dispose();
                // Other raw StreamingAssets can still be APK-compressed. They
                // are expanded in memory on demand, never into persistentDataPath.
                var bytes = new byte[checked((int)files[relative])];
                using var input = assets.Open(path);
                input.ReadExactly(bytes);
                if (input.ReadByte() != -1) throw new InvalidDataException("Packaged asset length mismatch.");
                return new MemoryStream(bytes, writable: false);
            }
            catch { descriptor?.Close(); descriptor?.Dispose(); throw; }
        });
    }

    private sealed class AssetOwner(AssetFileDescriptor descriptor) : IDisposable
    {
        public void Dispose() { try { descriptor.Close(); } finally { descriptor.Dispose(); } }
    }
}
