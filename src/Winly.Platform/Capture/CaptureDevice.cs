using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Winly.Platform.Capture;

[GeneratedComInterface]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
internal partial interface IDirect3DDxgiInterfaceAccess
{
    [PreserveSig]
    int GetInterface(in Guid interfaceId, out nint result);
}

/// <summary>A D3D11 device shared with Windows.Graphics.Capture, plus GPU → CPU readback of a captured surface.</summary>
internal sealed unsafe partial class CaptureDevice : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;

    private CaptureDevice(ID3D11Device device, IDirect3DDevice winRtDevice)
    {
        _device = device;
        _context = device.ImmediateContext;
        WinRtDevice = winRtDevice;
    }

    public IDirect3DDevice WinRtDevice { get; }

    public static CaptureDevice Create()
    {
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, null, out ID3D11Device? device).CheckError();
        using var dxgiDevice = device!.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var inspectable));
        try
        {
            return new CaptureDevice(device, MarshalInterface<IDirect3DDevice>.FromAbi(inspectable));
        }
        finally
        {
            Marshal.Release(inspectable);
        }
    }

    /// <summary>Copies the surface into a staging texture and returns tightly packed BGRA rows.</summary>
    public (byte[] Bgra, int Width, int Height) ReadPixels(IDirect3DSurface surface)
    {
        using var texture = TextureFrom(surface);
        var description = texture.Description;
        var stagingDescription = description with
        {
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read,
            BindFlags = BindFlags.None,
            MiscFlags = ResourceOptionFlags.None,
        };

        using var staging = _device.CreateTexture2D(stagingDescription);
        _context.CopyResource(staging, texture);
        var mapped = _context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            int width = (int)description.Width, height = (int)description.Height, rowBytes = width * 4;
            var pixels = new byte[rowBytes * height];
            for (var row = 0; row < height; row++)
            {
                new ReadOnlySpan<byte>((byte*)mapped.DataPointer + row * (long)mapped.RowPitch, rowBytes)
                    .CopyTo(pixels.AsSpan(row * rowBytes, rowBytes));
            }

            return (pixels, width, height);
        }
        finally
        {
            _context.Unmap(staging, 0);
        }
    }

    private static ID3D11Texture2D TextureFrom(IDirect3DSurface surface)
    {
        var surfacePointer = MarshalInterface<IDirect3DSurface>.FromManaged(surface);
        try
        {
            var accessInterfaceId = typeof(IDirect3DDxgiInterfaceAccess).GUID;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(surfacePointer, in accessInterfaceId, out var accessPointer));
            try
            {
                var access = ComInterfaceMarshaller<IDirect3DDxgiInterfaceAccess>.ConvertToManaged((void*)accessPointer)
                    ?? throw new InvalidOperationException("IDirect3DDxgiInterfaceAccess is unavailable.");
                var textureInterfaceId = typeof(ID3D11Texture2D).GUID;
                Marshal.ThrowExceptionForHR(access.GetInterface(in textureInterfaceId, out var texturePointer));
                return new ID3D11Texture2D(texturePointer);
            }
            finally
            {
                Marshal.Release(accessPointer);
            }
        }
        finally
        {
            Marshal.Release(surfacePointer);
        }
    }

    public void Dispose()
    {
        WinRtDevice.Dispose();
        _context.Dispose();
        _device.Dispose();
    }

    [LibraryImport("d3d11.dll")]
    private static partial int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
}
