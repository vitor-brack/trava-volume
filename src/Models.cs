using System;
using System.Collections.Generic;

namespace TravaVolume
{
    public class Level
    {
        public float Volume;
        public bool Mute;
        public string Name;
    }

    public class Device
    {
        public string Id;
        public string Name;
        public IAudioEndpointVolume Volume;
        public IAudioSessionManager2 Sessions;
        public VolumeCallback Callback;
        public bool IsInput;
        public float Locked;
        public bool LockedMute;
        public int Mismatches;
        public DateTime LastBlockLog;
        public int Suppressed;
        public List<TopologyControl> Controls = new List<TopologyControl>();
    }

    // Controle interno de um aparelho, como o "Microfone" da aba Níveis do alto-falante
    // (quanto do microfone volta para o fone).
    public class TopologyControl
    {
        public string Key;
        public string Name;
        public string Source;
        public Device Owner;
        public IPart Part;
        public IAudioVolumeLevel Level;
        public IAudioMute Mute;
        public ControlCallback Callback;
        public float MinDb;
        public float MaxDb;
        public float[] LockedDb;
        public bool LockedMute;
        public bool Active;
        public int Mismatches;
        public DateTime LastBlockLog;
        public int Suppressed;
    }

    public class SavedControl
    {
        public float[] Levels;
        public bool Mute;
        public string Name;
    }
}
