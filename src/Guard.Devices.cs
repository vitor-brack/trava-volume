using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TravaVolume
{
    public partial class Guard
    {
        // ------------------------------------------------------------ dispositivos

        void Scan()
        {
            if (enumerator == null) return;
            var seen = new HashSet<string>();
            ScanFlow(false, seen);   // saídas: alto-falantes e fones
            ScanFlow(true, seen);    // entradas: microfones

            foreach (string id in new List<string>(devices.Keys))
                if (!seen.Contains(id)) Detach(id, true);

            if (dirty)
            {
                SaveLevels();
                dirty = false;
            }
        }

        void ScanFlow(bool input, HashSet<string> seen)
        {
            IMMDeviceCollection collection;
            if (enumerator.EnumAudioEndpoints(input ? 1 : 0, 1, out collection) != 0 || collection == null)
            {
                // Se a lista falhar desta vez, não solta os dispositivos que já estão protegidos.
                foreach (Device d in devices.Values)
                    if (d.IsInput == input) seen.Add(d.Id);
                return;
            }
            uint count;
            collection.GetCount(out count);
            for (uint i = 0; i < count; i++)
            {
                IMMDevice device;
                if (collection.Item(i, out device) != 0 || device == null) continue;
                string id;
                if (device.GetId(out id) == 0 && id != null && !failed.Contains(id))
                {
                    seen.Add(id);
                    Device known;
                    if (devices.TryGetValue(id, out known))
                    {
                        Verify(known);
                    }
                    else
                    {
                        known = Attach(device, id, input);
                        if (known != null) devices[id] = known;
                    }
                }
                Marshal.ReleaseComObject(device);
            }
            Marshal.ReleaseComObject(collection);
        }

        Device Attach(IMMDevice device, string id, bool input)
        {
            try
            {
                var d = new Device();
                d.Id = id;
                d.IsInput = input;
                d.Name = FriendlyName(device);

                Guid volumeId = typeof(IAudioEndpointVolume).GUID;
                object instance;
                Marshal.ThrowExceptionForHR(device.Activate(ref volumeId, 23, IntPtr.Zero, out instance));
                d.Volume = (IAudioEndpointVolume)instance;

                Guid sessionsId = typeof(IAudioSessionManager2).GUID;
                object sessions;
                if (device.Activate(ref sessionsId, 23, IntPtr.Zero, out sessions) == 0)
                    d.Sessions = sessions as IAudioSessionManager2;

                float volume;
                bool mute;
                Marshal.ThrowExceptionForHR(d.Volume.GetMasterVolumeLevelScalar(out volume));
                Marshal.ThrowExceptionForHR(d.Volume.GetMute(out mute));

                Level level;
                if (enabled && saved.TryGetValue(id, out level))
                {
                    d.Locked = level.Volume;
                    d.LockedMute = input ? mute : level.Mute;
                    if (Differs(d, volume, mute))
                    {
                        Restore(d);
                        Log("RESTAURADO " + d.Name + ": estava em " + Pct(volume) + (mute ? " (silenciado)" : "") +
                            " ao ligar/conectar; voltei para " + Pct(d.Locked));
                        Notify("Volume restaurado", d.Name + " voltou para " + Pct(d.Locked) + " (estava em " + Pct(volume) + ").");
                    }
                }
                else
                {
                    d.Locked = volume;
                    d.LockedMute = mute;
                }
                Remember(d);

                d.Callback = new VolumeCallback(id, this);
                int hr = d.Volume.RegisterControlChangeNotify(d.Callback);
                if (hr != 0)
                    Log("Aviso: sem aviso instantâneo para " + d.Name + " (0x" + hr.ToString("X8") + "); vou conferir a cada 3 s");

                Log((enabled ? "Protegendo " : "Acompanhando (trava desligada) ") + d.Name + " em " + Pct(d.Locked) + (d.LockedMute ? " (silenciado)" : ""));
                if (!input) AttachControls(d, device);
                return d;
            }
            catch (Exception ex)
            {
                failed.Add(id);
                Log("Erro ao preparar o dispositivo " + id + ": " + ex.Message);
                return null;
            }
        }

        void Detach(string id, bool log)
        {
            Device d = devices[id];
            devices.Remove(id);
            try { if (d.Callback != null) d.Volume.UnregisterControlChangeNotify(d.Callback); } catch { }
            foreach (TopologyControl c in d.Controls)
            {
                c.Active = false;
                try { if (c.Callback != null) c.Part.UnregisterControlChangeCallback(c.Callback); } catch { }
            }
            try { Marshal.ReleaseComObject(d.Volume); } catch { }
            try { if (d.Sessions != null) Marshal.ReleaseComObject(d.Sessions); } catch { }
            if (log) Log("Desconectado: " + d.Name);
        }

        // Rede de segurança caso algum aviso instantâneo se perca.
        void Verify(Device d)
        {
            foreach (TopologyControl c in d.Controls) VerifyControl(c);
            float volume;
            bool mute;
            if (d.Volume.GetMasterVolumeLevelScalar(out volume) != 0 || d.Volume.GetMute(out mute) != 0) return;
            if (!Differs(d, volume, mute))
            {
                d.Mismatches = 0;
                return;
            }
            if (++d.Mismatches < 2) return;
            Decide(d, volume, mute, Guid.Empty, Environment.TickCount, false);
        }
    }
}
