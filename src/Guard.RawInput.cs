using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TravaVolume
{
    public partial class Guard
    {
        // ------------------------------------------------------------ teclas de volume

        void RegisterRawInput(IntPtr target)
        {
            var list = new Native.RawInputDevice[2];
            list[0].UsagePage = 0x01; list[0].Usage = 0x06; list[0].Flags = Native.RIDEV_INPUTSINK; list[0].Target = target;   // teclados
            list[1].UsagePage = 0x0C; list[1].Usage = 0x01; list[1].Flags = Native.RIDEV_INPUTSINK; list[1].Target = target;   // controles de mídia (HID)
            if (!Native.RegisterRawInputDevices(list, (uint)list.Length, (uint)Marshal.SizeOf(typeof(Native.RawInputDevice))))
                Log("Aviso: não consegui acompanhar as teclas de volume (erro " + Marshal.GetLastWin32Error() + ")");
        }

        // Só olha teclas de volume e sinais de mídia; qualquer outra tecla é descartada sem registro.
        public void OnRawInput(IntPtr handle, int time)
        {
            uint headerSize = (uint)(8 + 2 * IntPtr.Size);
            uint size = 0;
            Native.GetRawInputData(handle, Native.RID_INPUT, null, ref size, headerSize);
            if (size < headerSize || size > 8192) return;
            var data = new byte[size];
            if (Native.GetRawInputData(handle, Native.RID_INPUT, data, ref size, headerSize) == uint.MaxValue) return;

            int type = BitConverter.ToInt32(data, 0);
            IntPtr source = IntPtr.Size == 8 ? new IntPtr(BitConverter.ToInt64(data, 8)) : new IntPtr(BitConverter.ToInt32(data, 8));
            int body = (int)headerSize;

            if (type == Native.RIM_TYPEKEYBOARD && data.Length >= body + 8)
            {
                lastKeyboardTime = time;   // só o horário, para saber se a última ação foi no teclado ou no mouse
                ushort flags = BitConverter.ToUInt16(data, body + 2);
                ushort key = BitConverter.ToUInt16(data, body + 6);
                if (key < 0xAD || key > 0xAF || (flags & 1) != 0) return;   // só mudo / volume - / volume +, ao apertar
                Track(source, time, "tecla de volume");
            }
            else if (type == Native.RIM_TYPEHID && data.Length >= body + 8)
            {
                int reportSize = BitConverter.ToInt32(data, body);
                int reportCount = BitConverter.ToInt32(data, body + 4);
                int length = (int)Math.Min((long)reportSize * reportCount, data.Length - body - 8);
                bool any = false;
                var hex = new StringBuilder();
                for (int i = 0; i < length; i++)
                {
                    if (data[body + 8 + i] != 0) any = true;
                    hex.Append(data[body + 8 + i].ToString("X2")).Append(' ');
                }
                if (IsSuspect(source))
                {
                    if ((DateTime.Now - lastHidLog).TotalSeconds >= 2)
                    {
                        lastHidLog = DateTime.Now;
                        Log("Sinal de mídia vindo de " + SourceName(source) + ": " + hex.ToString().Trim());
                    }
                    Track(source, time, "sinal de mídia");
                }
                else if (any)
                {
                    Track(source, time, "tecla de mídia");
                }
            }
        }

        void Track(IntPtr source, int time, string what)
        {
            string name = SourceName(source);
            if (source == IntPtr.Zero || IsSuspect(source))
            {
                suspectTime = time;
                suspectSource = what + " de " + name;
            }
            else
            {
                userKeyTime = time;
                userKeySource = name;
            }
        }

        string InputPath(IntPtr source)
        {
            if (source == IntPtr.Zero) return "";
            string path;
            if (inputPaths.TryGetValue(source, out path)) return path;
            uint size = 0;
            Native.GetRawInputDeviceInfo(source, Native.RIDI_DEVICENAME, null, ref size);
            var text = new StringBuilder((int)size + 2);
            if (size > 0) Native.GetRawInputDeviceInfo(source, Native.RIDI_DEVICENAME, text, ref size);
            path = text.ToString().ToUpperInvariant();
            inputPaths[source] = path;
            return path;
        }

        bool IsSuspect(IntPtr source)
        {
            return source != IntPtr.Zero && InputPath(source).Contains(SuspectDevice);
        }

        string SourceName(IntPtr source)
        {
            if (source == IntPtr.Zero) return "um programa (tecla simulada)";
            string path = InputPath(source);
            if (path.Contains(SuspectDevice)) return "HyperX Quadcast";
            if (path.Contains("VID_046D&PID_C539")) return "mouse G502";
            if (path.Contains("VID_046D&PID_C232")) return "G HUB";
            if (path.Contains("PNP0303")) return "teclado";
            int vid = path.IndexOf("VID_", StringComparison.Ordinal);
            return vid >= 0 ? "dispositivo " + path.Substring(vid, Math.Min(17, path.Length - vid)) : "teclado";
        }
    }
}
