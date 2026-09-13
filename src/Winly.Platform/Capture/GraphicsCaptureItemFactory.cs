using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Graphics.Capture;

namespace Winly.Platform.Capture;

[GeneratedComInterface]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
internal partial interface IGraphicsCaptureItemInterop
{
    [PreserveSig]
    int CreateForWindow(nint window, in Guid interfaceId, out nint result);

    [PreserveSig]
    int CreateForMonitor(nint monitor, in Guid interfaceId, out nint result);
}

/// <summary>Creates a <see cref="GraphicsCaptureItem"/> for a monitor handle with no picker UI (research.md §1).</summary>
internal static unsafe partial class GraphicsCaptureItemFactory
{
    private static readonly Guid GraphicsCaptureItemInterfaceId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static GraphicsCaptureItem CreateForMonitor(nint monitorHandle)
    {
        var factoryPointer = GetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem", typeof(IGraphicsCaptureItemInterop).GUID);
        try
        {
            var interop = ComInterfaceMarshaller<IGraphicsCaptureItemInterop>.ConvertToManaged((void*)factoryPointer)
                ?? throw new InvalidOperationException("IGraphicsCaptureItemInterop is unavailable.");
            Marshal.ThrowExceptionForHR(interop.CreateForMonitor(monitorHandle, in GraphicsCaptureItemInterfaceId, out var itemPointer));
            try
            {
                return GraphicsCaptureItem.FromAbi(itemPointer);
            }
            finally
            {
                Marshal.Release(itemPointer);
            }
        }
        finally
        {
            Marshal.Release(factoryPointer);
        }
    }

    private static nint GetActivationFactory(string runtimeClassName, Guid interfaceId)
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString(runtimeClassName, runtimeClassName.Length, out var classNameHandle));
        try
        {
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(classNameHandle, in interfaceId, out var factory));
            return factory;
        }
        finally
        {
            WindowsDeleteString(classNameHandle);
        }
    }

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WindowsCreateString(string source, int length, out nint handle);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(nint handle);

    [LibraryImport("combase.dll")]
    private static partial int RoGetActivationFactory(nint runtimeClassName, in Guid interfaceId, out nint factory);
}
