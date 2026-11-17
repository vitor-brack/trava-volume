using System;
using System.Runtime.InteropServices;

namespace TravaVolume
{
    // Controles internos do aparelho (os extras da aba Níveis, como o retorno do microfone no fone).

    [Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDeviceTopology
    {
        [PreserveSig] int GetConnectorCount(out uint count);
        [PreserveSig] int GetConnector(uint index, out IConnector connector);
    }

    [Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IConnector
    {
        [PreserveSig] int GetConnectorType(out int type);
        [PreserveSig] int GetDataFlow(out int flow);
        [PreserveSig] int ConnectTo(IConnector other);
        [PreserveSig] int Disconnect();
        [PreserveSig] int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);
        [PreserveSig] int GetConnectedTo(out IConnector other);
    }

    [Guid("AE2DE0E4-5BCA-4F2D-AA46-5D13F8FDB3A9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPart
    {
        [PreserveSig] int GetName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int GetLocalId(out uint id);
        [PreserveSig] int GetGlobalId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetPartType(out int type);
        [PreserveSig] int GetSubType(out Guid subType);
        [PreserveSig] int GetControlInterfaceCount(out uint count);
        [PreserveSig] int GetControlInterface(uint index, out IntPtr control);
        [PreserveSig] int EnumPartsIncoming(out IPartsList parts);
        [PreserveSig] int EnumPartsOutgoing(out IPartsList parts);
        [PreserveSig] int GetTopologyObject(out IntPtr topology);
        [PreserveSig] int Activate(int clsCtx, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int RegisterControlChangeCallback(ref Guid iid, IControlChangeNotify notify);
        [PreserveSig] int UnregisterControlChangeCallback(IControlChangeNotify notify);
    }

    [Guid("6DAA848C-5EB0-45CC-AEA5-998A2CDA1FFB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPartsList
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetPart(uint index, out IPart part);
    }

    [Guid("7FB7B48F-531D-44A2-BCB3-5AD5A134B3DC"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioVolumeLevel
    {
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int GetLevelRange(uint channel, out float minDb, out float maxDb, out float stepDb);
        [PreserveSig] int GetLevel(uint channel, out float levelDb);
        [PreserveSig] int SetLevel(uint channel, float levelDb, ref Guid context);
    }

    [Guid("DF45AEEA-B74A-4B6B-AFAD-2366B6AA012E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioMute
    {
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [Guid("A09513ED-C709-4D21-BD7B-5F34C47F3947"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IControlChangeNotify
    {
        [PreserveSig] int OnNotify(uint senderProcessId, IntPtr context);
    }
}
