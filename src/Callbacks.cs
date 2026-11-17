using System;
using System.Runtime.InteropServices;

namespace TravaVolume
{
    public class ControlCallback : IControlChangeNotify
    {
        private readonly TopologyControl control;
        private readonly Guard owner;

        public ControlCallback(TopologyControl control, Guard owner)
        {
            this.control = control;
            this.owner = owner;
        }

        // O Windows informa qual processo fez a mudança.
        public int OnNotify(uint senderProcessId, IntPtr context)
        {
            try
            {
                Guid guid = context == IntPtr.Zero ? Guid.Empty : (Guid)Marshal.PtrToStructure(context, typeof(Guid));
                owner.OnControlNotification(control, senderProcessId, guid);
            }
            catch { }
            return 0;
        }
    }

    public class VolumeCallback : IAudioEndpointVolumeCallback
    {
        private readonly string id;
        private readonly Guard owner;

        public VolumeCallback(string id, Guard owner)
        {
            this.id = id;
            this.owner = owner;
        }

        public int OnNotify(IntPtr data)
        {
            try
            {
                var n = (AudioVolumeNotificationData)Marshal.PtrToStructure(data, typeof(AudioVolumeNotificationData));
                owner.OnVolumeNotification(id, n.Context, n.Muted != 0, n.MasterVolume);
            }
            catch { }
            return 0;
        }
    }
}
