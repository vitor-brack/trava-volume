using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace TravaVolume
{
    public partial class Guard
    {
        // ------------------------------------------------------------ utilidades

        static bool Differs(Device d, float volume, bool mute)
        {
            return Math.Abs(volume - d.Locked) >= Tolerance || mute != d.LockedMute;
        }

        static string Describe(Device d, float volume, bool mute)
        {
            var parts = new List<string>();
            if (Math.Abs(volume - d.Locked) >= Tolerance) parts.Add(Pct(d.Locked) + " → " + Pct(volume));
            if (mute != d.LockedMute) parts.Add(mute ? "silenciado" : "som reativado");
            return string.Join(", ", parts.ToArray());
        }

        static string Pct(float value)
        {
            return ((int)Math.Round(value * 100)).ToString(CultureInfo.InvariantCulture) + "%";
        }

        static int Elapsed(int from, int to)
        {
            long diff = Math.Abs((long)unchecked(to - from));
            return (int)Math.Min(diff, int.MaxValue);
        }

        static string WindowProcess(IntPtr window)
        {
            if (window == IntPtr.Zero) return "nenhum";
            uint pid;
            Native.GetWindowThreadProcessId(window, out pid);
            return ProcessName(pid);
        }

        static string ProcessName(uint pid)
        {
            try
            {
                using (var p = Process.GetProcessById((int)pid)) return p.ProcessName.ToLowerInvariant();
            }
            catch
            {
                return "processo " + pid;
            }
        }

        static string WindowClass(IntPtr window)
        {
            if (window == IntPtr.Zero) return "";
            var name = new StringBuilder(256);
            Native.GetClassName(window, name, name.Capacity);
            return name.ToString();
        }

        static bool IsVolumeUi(string process)
        {
            return Array.IndexOf(VolumeUi, process) >= 0;
        }

        // Parte da barra de tarefas onde ficam os ícones da bandeja (inclusive o de volume).
        static bool OverTrayArea(IntPtr taskbar, Native.Point cursor)
        {
            Native.Rect r;
            if (!Native.GetWindowRect(taskbar, out r)) return false;
            int width = r.Right - r.Left;
            int height = r.Bottom - r.Top;
            if (width >= height) return cursor.X >= r.Right - Math.Max(width * 35 / 100, 200);
            return cursor.Y >= r.Bottom - Math.Max(height * 35 / 100, 200);
        }

        static string ActiveApps(Device d)
        {
            if (d.Sessions == null) return "?";
            var names = new List<string>();
            IAudioSessionEnumerator sessions = null;
            try
            {
                if (d.Sessions.GetSessionEnumerator(out sessions) != 0 || sessions == null) return "?";
                int count;
                sessions.GetCount(out count);
                for (int i = 0; i < count; i++)
                {
                    object item;
                    if (sessions.GetSession(i, out item) != 0 || item == null) continue;
                    var control = item as IAudioSessionControl2;
                    int state;
                    uint pid;
                    if (control != null && control.GetState(out state) == 0 && state == 1 &&
                        control.GetProcessId(out pid) >= 0 && pid != 0)
                    {
                        string name = ProcessName(pid);
                        if (!names.Contains(name)) names.Add(name);
                    }
                    Marshal.ReleaseComObject(item);
                }
            }
            catch { }
            finally
            {
                if (sessions != null) Marshal.ReleaseComObject(sessions);
            }
            return names.Count == 0 ? "nada" : string.Join(", ", names.ToArray());
        }

        static string FriendlyName(IMMDevice device)
        {
            IPropertyStore store = null;
            try
            {
                if (device.OpenPropertyStore(0, out store) != 0 || store == null) return "saída de áudio";
                var key = new PropertyKey();
                key.FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
                key.PropertyId = 14;   // PKEY_Device_FriendlyName
                PropVariant value;
                if (store.GetValue(ref key, out value) != 0) return "saída de áudio";
                string name = value.Type == 31 ? Marshal.PtrToStringUni(value.Value) : null;   // VT_LPWSTR
                Native.PropVariantClear(ref value);
                return string.IsNullOrEmpty(name) ? "saída de áudio" : name;
            }
            catch
            {
                return "saída de áudio";
            }
            finally
            {
                if (store != null) Marshal.ReleaseComObject(store);
            }
        }
    }
}
