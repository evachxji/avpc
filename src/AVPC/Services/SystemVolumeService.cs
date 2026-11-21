using System.Runtime.InteropServices;

namespace AVPC.Services;

public sealed class SystemVolumeService : IDisposable
{
    private const uint ClsCtxAll = 23;
    private readonly IAudioEndpointVolume _endpoint;
    private bool _disposed;

    public SystemVolumeService()
    {
        var enumeratorType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))
            ?? throw new InvalidOperationException("系统音频设备枚举器不可用。");
        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;
        IMMDevice? device = null;
        try
        {
            ThrowIfFailed(enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device));
            var interfaceId = typeof(IAudioEndpointVolume).GUID;
            ThrowIfFailed(device.Activate(ref interfaceId, ClsCtxAll, IntPtr.Zero, out var instance));
            _endpoint = (IAudioEndpointVolume)instance;
        }
        finally
        {
            ReleaseComObject(device);
            ReleaseComObject(enumerator);
        }
    }

    public double Volume
    {
        get
        {
            ThrowIfFailed(_endpoint.GetMasterVolumeLevelScalar(out var volume));
            return Math.Clamp(volume, 0, 1);
        }
        set
        {
            var eventContext = Guid.Empty;
            ThrowIfFailed(_endpoint.SetMasterVolumeLevelScalar((float)Math.Clamp(value, 0, 1), ref eventContext));
        }
    }

    private static void ThrowIfFailed(int result)
    {
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseComObject(_endpoint);
    }

    private static void ReleaseComObject(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
            Marshal.FinalReleaseComObject(instance);
    }

    private enum EDataFlow
    {
        Render
    }

    private enum ERole
    {
        Console,
        Multimedia
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid interfaceId, uint classContext, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IntPtr notify);

        [PreserveSig]
        int UnregisterControlChangeNotify(IntPtr notify);

        [PreserveSig]
        int GetChannelCount(out uint channelCount);

        [PreserveSig]
        int SetMasterVolumeLevel(float level, ref Guid eventContext);

        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

        [PreserveSig]
        int GetMasterVolumeLevel(out float level);

        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);
    }
}
