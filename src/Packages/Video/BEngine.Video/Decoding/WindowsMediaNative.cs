using System.Runtime.InteropServices;

namespace BEngine.Video;

// Media Foundation COM ABI from the Windows SDK. Raw owned references avoid RCW identity
// sharing between independent readers and do not require COM interface generation at runtime.
internal static class WindowsMediaNative
{
    internal const uint VideoStream = 0xFFFFFFFC, AudioStream = 0xFFFFFFFD, AllStreams = 0xFFFFFFFE, MediaSource = 0xFFFFFFFF;
    internal static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"), Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    internal static readonly Guid VideoType = new("73646976-0000-0010-8000-00aa00389b71"), AudioType = new("73647561-0000-0010-8000-00aa00389b71");
    internal static readonly Guid Nv12 = new("3231564e-0000-0010-8000-00aa00389b71"), FloatAudio = new("00000003-0000-0010-8000-00aa00389b71");
    internal static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d"), FrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    internal static readonly Guid DisplayAperture = new("d7388766-18fe-48c6-a177-ee894867c8c4");
    internal static readonly Guid Rotation = new("c380465d-2271-428c-9b83-ecea3b4a85c1");
    internal static readonly Guid Duration = new("6c990d33-bb8e-477a-8598-0d5d96fcd88a"), Characteristics = new("6d23f5c8-c5d7-4a9b-9971-5d11f8bca880");
    internal static readonly Guid Channels = new("37e48bf5-645e-4c5b-89de-ada9e29b696a"), SampleRate = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    internal static readonly Guid Bits = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669"), Align = new("322de230-9eeb-43bd-ab7a-ff412251541d"), BytesPerSecond = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
    internal static readonly Guid Stride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6"), Matrix = new("3e23d450-2c75-4d25-a00e-b91670d12327"), Range = new("c21b8ee5-b956-4071-8daf-325edf5cab11");
    internal static readonly Guid D3DManager = new("ec822da2-e1e9-4b29-a0d8-563c719f5269"), DisableDxva = new("aa456cfd-3943-4a1e-a77d-1838c0ea2e35"), HardwareTransforms = new("a634a91c-822b-41b9-a494-4de4643612b0");
    internal static readonly Guid Buffer2D = new("7dc9d5f9-9ed9-44ec-9bbf-0600bb589fbb"), DxgiBuffer = new("e7174cfa-1c9e-48b1-8866-626226bfc258");

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct Variant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public long Value; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int Query(nint self, in Guid iid, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate uint ReleaseCall(nint self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int Simple(nint self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int Get32(nint self, in Guid key, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int Get64(nint self, in Guid key, out ulong value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int GetBlob(nint self, in Guid key, [Out] byte[] buffer, uint size, out uint actual);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int Set32(nint self, in Guid key, uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetGuid(nint self, in Guid key, in Guid value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetUnknown(nint self, in Guid key, nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SelectStream(nint self, uint stream, int selected);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int NativeType(nint self, uint stream, uint index, out nint type);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int CurrentType(nint self, uint stream, out nint type);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetType(nint self, uint stream, nint reserved, nint type);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int Position(nint self, in Guid format, in Variant value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int ReadSample(nint self, uint stream, uint control, out uint actual, out uint flags, out long time, out nint sample);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int Presentation(nint self, uint stream, in Guid key, out Variant value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int OutPointer(nint self, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int BufferAt(nint self, uint index, out nint buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int Out32(nint self, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int BufferLock(nint self, out nint bytes, out uint capacity, out uint length);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int CopyBuffer(nint self, [Out] byte[] bytes, uint size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int ResetDevice(nint self, nint device, uint token);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetMultithread(nint self, int protect);

    internal static T Method<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));
    internal static void Check(int hr, string operation) { if (hr < 0) throw new InvalidDataException($"{operation} failed (0x{hr:X8}).", Marshal.GetExceptionForHR(hr)); }
    internal static void Release(ref nint value) { var instance = value; value = 0; if (instance != 0) Method<ReleaseCall>(instance, 2)(instance); }
    internal static uint UInt32(nint attributes, Guid key, uint fallback = 0) => Method<Get32>(attributes, 7)(attributes, key, out var value) >= 0 ? value : fallback;
    internal static ulong UInt64(nint attributes, Guid key) { Check(Method<Get64>(attributes, 8)(attributes, key, out var value), "Read media attribute"); return value; }
    internal static void Set(nint attributes, Guid key, uint value) => Check(Method<Set32>(attributes, 21)(attributes, key, value), "Set media attribute");
    internal static void Set(nint attributes, Guid key, Guid value) => Check(Method<SetGuid>(attributes, 24)(attributes, key, value), "Set media type");

    [DllImport("ole32.dll", ExactSpelling = true)] internal static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll", ExactSpelling = true)] internal static extern void CoUninitialize();
    [DllImport("ole32.dll", ExactSpelling = true)] internal static extern int PropVariantClear(ref Variant value);
    [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFShutdown();
    [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFCreateAttributes(out nint attributes, uint count);
    [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFCreateMediaType(out nint type);
    [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFCreateDXGIDeviceManager(out uint token, out nint manager);
    [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFCreateMFByteStreamOnStream(nint stream, out nint byteStream);
    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] internal static extern int MFCreateSourceReaderFromURL(string url, nint attributes, out nint reader);
    [DllImport("mfreadwrite.dll", ExactSpelling = true)] internal static extern int MFCreateSourceReaderFromByteStream(nint stream, nint attributes, out nint reader);
    [DllImport("shlwapi.dll", ExactSpelling = true)] internal static extern nint SHCreateMemStream(byte[] bytes, uint count);
    [DllImport("d3d11.dll", ExactSpelling = true)] internal static extern int D3D11CreateDevice(nint adapter, uint driverType, nint software, uint flags, nint levels, uint levelCount, uint sdkVersion, out nint device, out uint featureLevel, out nint context);
}
