using System.Runtime.InteropServices;

namespace BEngine.Platform;

[Flags]
internal enum UiaProviderOptions { ServerSideProvider = 1, UseComThreading = 0x20 }
internal enum UiaNavigateDirection { Parent, NextSibling, PreviousSibling, FirstChild, LastChild }
internal enum UiaToggleState { Off, On, Indeterminate }

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct UiaRect(double Left, double Top, double Width, double Height);

[ComVisible(true), Guid("d6dd68d1-86fd-4332-8666-9abedea2d24c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRawElementProviderSimple
{
    UiaProviderOptions ProviderOptions { get; }
    [return: MarshalAs(UnmanagedType.Interface)] object? GetPatternProvider(int patternId);
    [return: MarshalAs(UnmanagedType.Struct)] object? GetPropertyValue(int propertyId);
    IRawElementProviderSimple? HostRawElementProvider { get; }
}

[ComVisible(true), Guid("f7063da8-8359-439c-9297-bbc5299a7d87"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRawElementProviderFragment : IRawElementProviderSimple
{
    IRawElementProviderFragment? Navigate(UiaNavigateDirection direction);
    int[] GetRuntimeId();
    UiaRect BoundingRectangle { get; }
    IRawElementProviderSimple[]? GetEmbeddedFragmentRoots();
    void SetFocus();
    IRawElementProviderFragmentRoot FragmentRoot { get; }
}

[ComVisible(true), Guid("620ce2a5-ab8f-40a9-86cb-de3c75599b58"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRawElementProviderFragmentRoot : IRawElementProviderFragment
{
    IRawElementProviderFragment? ElementProviderFromPoint(double x, double y);
    IRawElementProviderFragment? GetFocus();
}

[ComVisible(true), Guid("54fcb24b-e18e-47a2-b4d3-eccbe77599a2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInvokeProvider { void Invoke(); }

[ComVisible(true), Guid("56d00bd0-c4f4-433c-a836-1a52a57e0892"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IToggleProvider { void Toggle(); UiaToggleState ToggleState { get; } }

[ComVisible(true), Guid("36dc7aef-33e6-4691-afe1-2be7274b3d33"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRangeValueProvider
{
    void SetValue(double value);
    double Value { get; }
    bool IsReadOnly { get; }
    double Maximum { get; }
    double Minimum { get; }
    double LargeChange { get; }
    double SmallChange { get; }
}
