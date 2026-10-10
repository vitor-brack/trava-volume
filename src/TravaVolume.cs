// Trava de volume: mantém o volume das saídas de áudio e dos microfones no nível que você escolheu.
// Mudanças feitas por você (teclas de volume, ícone de volume da barra de tarefas,
// Configurações rápidas, Configurações de som, Mixer de volume) são aceitas; qualquer outra é
// desfeita na hora e anotada em %LOCALAPPDATA%\TravaVolume\registro.txt.
// Também trava os controles extras da aba Níveis (ex.: o retorno do microfone no fone).
// O mudo dos microfones nunca é travado: desfazer um "mudo" poderia reativar o microfone sem você saber.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Trava de volume")]
[assembly: AssemblyProduct("Trava de volume")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace TravaVolume
{
    // ------------------------------------------------------------------ Core Audio

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    public class MMDeviceEnumeratorComObject { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey
    {
        public Guid FormatId;
        public int PropertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PropVariant
    {
        public ushort Type;
        public ushort Reserved1, Reserved2, Reserved3;
        public IntPtr Value;
        public IntPtr Value2;
    }

    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr data);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioVolumeNotificationData
    {
        public Guid Context;
        public int Muted;
        public float MasterVolume;
        public uint Channels;
    }

    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionId, int flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionId, int flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }

    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid grouping);
        [PreserveSig] int SetGroupingParam(ref Guid grouping, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint processId);
    }

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

    // ------------------------------------------------------------------ Win32

    public static class Native
    {
        public const int WM_INPUT = 0x00FF;
        public const uint RIDEV_INPUTSINK = 0x00000100;
        public const uint RID_INPUT = 0x10000003;
        public const uint RIDI_DEVICENAME = 0x20000007;
        public const int RIM_TYPEKEYBOARD = 1;
        public const int RIM_TYPEHID = 2;
        public const uint GA_ROOT = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct RawInputDevice
        {
            public ushort UsagePage;
            public ushort Usage;
            public uint Flags;
            public IntPtr Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LastInputInfo
        {
            public uint Size;
            public uint Time;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr window, out Rect rect);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);

        [DllImport("user32.dll")]
        public static extern uint GetRawInputData(IntPtr rawInput, uint command, byte[] data, ref uint size, uint headerSize);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder data, ref uint size);

        [DllImport("user32.dll")]
        public static extern int GetMessageTime();

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(Point point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr window, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr window, StringBuilder name, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        public static extern bool GetLastInputInfo(ref LastInputInfo info);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr icon);

        [DllImport("ole32.dll")]
        public static extern int PropVariantClear(ref PropVariant value);
    }

    // ------------------------------------------------------------------ Programa

    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            bool first;
            using (var mutex = new Mutex(true, @"Local\TravaVolume.Instancia", out first))
            {
                if (!first) return;
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var guard = new Guard();
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { guard.Log("Erro: " + e.Exception); };
                Application.Run(guard);
            }
        }
    }

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

    // Janela invisível que recebe as teclas de volume/mídia de todos os dispositivos.
    public class InputWindow : Form
    {
        public Guard Handler;

        public InputWindow()
        {
            ShowInTaskbar = false;
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_INPUT && Handler != null)
                Handler.OnRawInput(m.LParam, Native.GetMessageTime());
            base.WndProc(ref m);
        }
    }

    public class Guard : ApplicationContext
    {
        const float Tolerance = 0.005f;
        const float DbTolerance = 0.15f;
        const string SuspectDevice = "VID_0951&PID_16DF";   // HyperX Quadcast
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "TravaVolume";
        static readonly Guid OwnContext = new Guid("6c0f3b5e-2a41-4f5b-9d27-7a1e0c9b4d11");

        // Processos que mostram os controles de volume do próprio Windows.
        static readonly string[] VolumeUi =
        {
            "shellexperiencehost", "shellhost", "systemsettings", "applicationframehost",
            "sndvol", "rundll32", "gamebar", "xboxgamebarwidgets"
        };

        readonly string dir;
        readonly string logPath;
        readonly string levelsPath;
        readonly string configPath;
        readonly string controlsPath;
        readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;
        readonly Dictionary<string, Device> devices = new Dictionary<string, Device>();
        readonly Dictionary<string, Level> saved = new Dictionary<string, Level>();
        readonly Dictionary<string, SavedControl> savedControls = new Dictionary<string, SavedControl>();
        readonly Dictionary<IntPtr, string> inputPaths = new Dictionary<IntPtr, string>();
        readonly HashSet<string> failed = new HashSet<string>();
        // "Contextos" (identificação que cada programa manda junto com a mudança) já pegos mexendo.
        readonly HashSet<Guid> blockedContexts = new HashSet<Guid>();
        readonly InputWindow window;
        readonly System.Windows.Forms.Timer timer;
        IMMDeviceEnumerator enumerator;
        NotifyIcon tray;
        ContextMenuStrip menu;
        bool dirty;
        bool enabled = true;
        Font boldFont;
        DateTime lastBalloon = DateTime.MinValue;
        DateTime lastHidLog = DateTime.MinValue;
        int userKeyTime;
        string userKeySource;
        int suspectTime;
        string suspectSource;
        int lastKeyboardTime;

        public Guard()
        {
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TravaVolume");
            Directory.CreateDirectory(dir);
            logPath = Path.Combine(dir, "registro.txt");
            levelsPath = Path.Combine(dir, "niveis.txt");
            configPath = Path.Combine(dir, "config.txt");
            controlsPath = Path.Combine(dir, "controles.txt");
            Log("==== Trava de volume iniciada ====");
            LoadLevels();
            LoadControls();
            LoadConfig();
            if (!enabled) Log("A trava está DESLIGADA (você deixou assim)");

            window = new InputWindow();
            window.Handler = this;
            RegisterRawInput(window.Handle);

            BuildTray();
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                Scan();
            }
            catch (Exception ex)
            {
                Log("Erro ao acessar o áudio do Windows: " + ex.Message);
            }

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 3000;
            timer.Tick += delegate { Scan(); };
            timer.Start();
        }

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

        // ------------------------------------------------------------ controles extras (aba Níveis)

        // O volume principal fica no caminho que vem do PC e já é travado pelo volume normal;
        // os extras ficam nos caminhos que vêm de entradas físicas (microfone, entrada de linha...).
        void AttachControls(Device d, IMMDevice device)
        {
            try
            {
                Guid topologyId = typeof(IDeviceTopology).GUID;
                object instance;
                if (device.Activate(ref topologyId, 23, IntPtr.Zero, out instance) != 0 || instance == null) return;
                IConnector endpointSide;
                IConnector deviceSide;
                if (((IDeviceTopology)instance).GetConnector(0, out endpointSide) != 0 || endpointSide == null) return;
                if (endpointSide.GetConnectedTo(out deviceSide) != 0 || deviceSide == null) return;

                var paths = new List<List<IPart>>();
                CollectPaths((IPart)deviceSide, new List<IPart>(), paths, 0);

                var fromComputer = new HashSet<string>();
                foreach (List<IPart> path in paths)
                {
                    int type = ConnectorType(path[path.Count - 1]);
                    if (type == 3 || type == 4)
                        foreach (IPart part in path) fromComputer.Add(GlobalId(part));
                }
                foreach (List<IPart> path in paths)
                {
                    IPart origin = path[path.Count - 1];
                    int type = ConnectorType(origin);
                    if (type != 1 && type != 2) continue;
                    string source = PartName(origin);
                    foreach (IPart part in path)
                    {
                        string id = GlobalId(part);
                        if (!fromComputer.Contains(id)) AddControl(d, part, id, source);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Aviso: não consegui ler os controles extras de " + d.Name + ": " + ex.Message);
            }
        }

        static void CollectPaths(IPart part, List<IPart> current, List<List<IPart>> paths, int depth)
        {
            if (depth > 40 || paths.Count > 200) return;
            var path = new List<IPart>(current);
            path.Add(part);
            IPartsList incoming;
            uint count = 0;
            if (part.EnumPartsIncoming(out incoming) == 0 && incoming != null) incoming.GetCount(out count);
            if (count == 0)
            {
                paths.Add(path);
                return;
            }
            for (uint i = 0; i < count; i++)
            {
                IPart next;
                if (incoming.GetPart(i, out next) == 0 && next != null) CollectPaths(next, path, paths, depth + 1);
            }
        }

        // 1/2 = entrada física, 3/4 = vem do PC, -1 = não é conector.
        static int ConnectorType(IPart part)
        {
            int partType;
            if (part.GetPartType(out partType) != 0 || partType != 0) return -1;
            var connector = part as IConnector;
            int type;
            return connector != null && connector.GetConnectorType(out type) == 0 ? type : -1;
        }

        static string PartName(IPart part)
        {
            string name;
            return part.GetName(out name) == 0 && !string.IsNullOrEmpty(name) ? name.Trim() : "controle";
        }

        static string GlobalId(IPart part)
        {
            string id;
            return part.GetGlobalId(out id) == 0 && id != null ? id : Guid.NewGuid().ToString();
        }

        void AddControl(Device d, IPart part, string globalId, string source)
        {
            string key = d.Id + "|" + globalId;
            foreach (TopologyControl existing in d.Controls)
                if (existing.Key == key) return;

            var c = new TopologyControl();
            c.Key = key;
            c.Owner = d;
            c.Part = part;
            c.Source = source;
            Guid levelId = typeof(IAudioVolumeLevel).GUID;
            Guid muteId = typeof(IAudioMute).GUID;
            object instance;
            if (part.Activate(23, ref levelId, out instance) == 0 && instance != null)
            {
                c.Level = (IAudioVolumeLevel)instance;
                float step;
                c.Level.GetLevelRange(0, out c.MinDb, out c.MaxDb, out step);
                c.Name = d.Name + " › " + source;
            }
            else if (part.Activate(23, ref muteId, out instance) == 0 && instance != null)
            {
                c.Mute = (IAudioMute)instance;
                c.Name = d.Name + " › " + source + " (mudo)";
            }
            else
            {
                return;
            }

            float[] levels;
            bool mute;
            if (!ReadControl(c, out levels, out mute)) return;
            SavedControl saved;
            if (enabled && savedControls.TryGetValue(key, out saved) &&
                (c.Level == null || (saved.Levels != null && saved.Levels.Length == levels.Length)))
            {
                c.LockedDb = saved.Levels;
                c.LockedMute = saved.Mute;
                if (ControlDiffers(c, levels, mute))
                {
                    string before = StateText(c, levels, mute);
                    RestoreControl(c);
                    Log("RESTAURADO " + c.Name + ": estava em " + before + " ao ligar/conectar; voltei para " + StateText(c, c.LockedDb, c.LockedMute));
                }
            }
            else
            {
                c.LockedDb = levels;
                c.LockedMute = mute;
            }
            RememberControl(c);

            c.Callback = new ControlCallback(c, this);
            Guid watched = c.Level != null ? levelId : muteId;
            int hr = part.RegisterControlChangeCallback(ref watched, c.Callback);
            if (hr != 0) Log("Aviso: sem aviso instantâneo para " + c.Name + " (0x" + hr.ToString("X8") + "); vou conferir a cada 3 s");
            c.Active = true;
            d.Controls.Add(c);
            Log((enabled ? "Protegendo " : "Acompanhando (trava desligada) ") + c.Name + " em " + StateText(c, c.LockedDb, c.LockedMute));
        }

        static bool ReadControl(TopologyControl c, out float[] levels, out bool mute)
        {
            levels = null;
            mute = false;
            if (c.Level == null) return c.Mute.GetMute(out mute) == 0;
            uint count;
            if (c.Level.GetChannelCount(out count) != 0 || count == 0 || count > 32) return false;
            levels = new float[count];
            for (uint i = 0; i < count; i++)
                if (c.Level.GetLevel(i, out levels[i]) != 0) return false;
            return true;
        }

        static bool ControlDiffers(TopologyControl c, float[] levels, bool mute)
        {
            if (c.Level == null) return mute != c.LockedMute;
            if (c.LockedDb == null || levels == null || levels.Length != c.LockedDb.Length) return true;
            for (int i = 0; i < levels.Length; i++)
                if (Math.Abs(levels[i] - c.LockedDb[i]) >= DbTolerance) return true;
            return false;
        }

        static string StateText(TopologyControl c, float[] levels, bool mute)
        {
            if (c.Level == null) return mute ? "silenciado" : "com som";
            return PctDb(c, levels);
        }

        static string PctDb(TopologyControl c, float[] levels)
        {
            if (levels == null || levels.Length == 0) return "?";
            float top = float.MinValue;
            foreach (float level in levels) if (level > top) top = level;
            return PctOfDb(c, top);
        }

        // Mesma escala da aba Níveis do Windows: a porcentagem é linear na amplitude,
        // entre o mínimo (0%) e o máximo (100%) do controle.
        static string PctOfDb(TopologyControl c, float db)
        {
            double low = Math.Pow(10, c.MinDb / 20.0);
            double high = Math.Pow(10, c.MaxDb / 20.0);
            if (high <= low) return "?";
            double fraction = (Math.Pow(10, db / 20.0) - low) / (high - low);
            fraction = Math.Max(0, Math.Min(1, fraction));
            return ((int)Math.Round(100 * fraction)).ToString(CultureInfo.InvariantCulture) + "%";
        }

        // Descreve a mudança pelo canal que mais mudou (os canais mudam um de cada vez).
        static string ChangeText(TopologyControl c, float[] levels, bool mute)
        {
            if (c.Level == null) return StateText(c, null, c.LockedMute) + " → " + StateText(c, null, mute);
            if (c.LockedDb == null || levels == null || c.LockedDb.Length == 0 || levels.Length == 0) return "?";
            int channel = 0;
            float biggest = -1;
            for (int i = 0; i < levels.Length && i < c.LockedDb.Length; i++)
            {
                float delta = Math.Abs(levels[i] - c.LockedDb[i]);
                if (delta > biggest)
                {
                    biggest = delta;
                    channel = i;
                }
            }
            return PctOfDb(c, c.LockedDb[channel]) + " → " + PctOfDb(c, levels[channel]);
        }

        void RestoreControl(TopologyControl c)
        {
            if (c.Level != null)
            {
                for (int i = 0; i < c.LockedDb.Length; i++)
                {
                    Guid context = OwnContext;
                    c.Level.SetLevel((uint)i, c.LockedDb[i], ref context);
                }
            }
            else
            {
                Guid context = OwnContext;
                c.Mute.SetMute(c.LockedMute, ref context);
            }
            c.Mismatches = 0;
        }

        void AcceptControl(TopologyControl c, float[] levels, bool mute)
        {
            if (c.Level != null) c.LockedDb = levels;
            else c.LockedMute = mute;
            c.Mismatches = 0;
            RememberControl(c);
        }

        void RememberControl(TopologyControl c)
        {
            SavedControl saved;
            if (!savedControls.TryGetValue(c.Key, out saved))
            {
                saved = new SavedControl();
                savedControls[c.Key] = saved;
            }
            saved.Levels = c.LockedDb;
            saved.Mute = c.LockedMute;
            saved.Name = c.Name;
            dirty = true;
        }

        // Chamado numa thread do Windows; a decisão acontece na thread da interface.
        public void OnControlNotification(TopologyControl c, uint sender, Guid context)
        {
            if (context == OwnContext || sender == ownPid) return;
            int time = Environment.TickCount;
            try { window.BeginInvoke(new MethodInvoker(delegate { DecideControl(c, sender, time); })); }
            catch { }
        }

        void DecideControl(TopologyControl c, uint sender, int time)
        {
            float[] levels;
            bool mute;
            if (!c.Active || !ReadControl(c, out levels, out mute) || !ControlDiffers(c, levels, mute)) return;
            if (!enabled)
            {
                AcceptControl(c, levels, mute);
                return;
            }
            string change = ChangeText(c, levels, mute);
            string who = sender == 0 ? "o próprio aparelho" : ProcessName(sender);
            // Aqui o Windows diz quem mexeu: só vale se foi a janela de Som do Windows, com você usando o PC.
            if (IsVolumeUi(who) && InputAge(time) <= 2000)
            {
                AcceptControl(c, levels, mute);
                Log("ACEITO     " + c.Name + ": " + change + " | controle de volume do Windows (" + who + ")");
                return;
            }
            RestoreControl(c);
            LogControlBlock(c, change, "alterado por: " + who);
        }

        void VerifyControl(TopologyControl c)
        {
            float[] levels;
            bool mute;
            if (!c.Active || !ReadControl(c, out levels, out mute)) return;
            if (!ControlDiffers(c, levels, mute))
            {
                c.Mismatches = 0;
                return;
            }
            if (++c.Mismatches < 2) return;
            if (!enabled)
            {
                AcceptControl(c, levels, mute);
                return;
            }
            string change = ChangeText(c, levels, mute);
            RestoreControl(c);
            LogControlBlock(c, change, "percebido na conferência periódica");
        }

        void LogControlBlock(TopologyControl c, string change, string how)
        {
            if ((DateTime.Now - c.LastBlockLog).TotalSeconds < 30)
            {
                c.Suppressed++;
                return;
            }
            string repeats = c.Suppressed > 0 ? " | +" + c.Suppressed + " tentativas desde o último registro" : "";
            c.Suppressed = 0;
            c.LastBlockLog = DateTime.Now;
            Log("BLOQUEADO  " + c.Name + ": " + change + " | " + how + repeats);
            Notify("Mudança de volume bloqueada", c.Name + ": " + change + ". Voltei para o nível travado.");
        }

        static int InputAge(int time)
        {
            var input = new Native.LastInputInfo();
            input.Size = (uint)Marshal.SizeOf(typeof(Native.LastInputInfo));
            return Native.GetLastInputInfo(ref input) ? Elapsed((int)input.Time, time) : int.MaxValue;
        }

        // Chamado numa thread do Windows; a decisão acontece na thread da interface.
        public void OnVolumeNotification(string id, Guid context, bool mute, float volume)
        {
            if (context == OwnContext) return;
            int time = Environment.TickCount;
            try
            {
                window.BeginInvoke(new MethodInvoker(delegate
                {
                    Device d;
                    if (devices.TryGetValue(id, out d)) Decide(d, volume, mute, context, time, true);
                }));
            }
            catch { }
        }

        void Decide(Device d, float volume, bool mute, Guid context, int time, bool live)
        {
            if (!Differs(d, volume, mute)) return;
            if (d.IsInput && mute != d.LockedMute)
            {
                d.LockedMute = mute;
                Remember(d);
                if (enabled) Log("LIVRE      " + d.Name + ": " + (mute ? "silenciado" : "reativado") + " (o mudo do microfone não é travado)");
                if (!Differs(d, volume, mute)) return;
            }
            if (!enabled)
            {
                Accept(d, volume, mute);   // desligada: só acompanha os níveis para travar os atuais quando religar
                return;
            }
            string change = Describe(d, volume, mute);
            string how = "percebido na conferência periódica";
            if (live && IsUserAction(d, time, context, out how))
            {
                Accept(d, volume, mute);
                Log("ACEITO     " + d.Name + ": " + change + " | " + how + " | contexto " + context);
                return;
            }
            Restore(d);
            if (context != Guid.Empty)
            {
                if (blockedContexts.Count > 500) blockedContexts.Clear();
                blockedContexts.Add(context);
            }
            // Programas com ajuste automático (ex.: ganho automático do microfone) tentam de novo sem parar;
            // registra no máximo uma linha a cada 30 s por dispositivo.
            if ((DateTime.Now - d.LastBlockLog).TotalSeconds < 30)
            {
                d.Suppressed++;
                return;
            }
            string repeats = d.Suppressed > 0 ? " | +" + d.Suppressed + " tentativas desde o último registro" : "";
            d.Suppressed = 0;
            d.LastBlockLog = DateTime.Now;
            string suspect = "";
            if (suspectSource != null && Elapsed(suspectTime, time) <= 2000 && !how.StartsWith("veio de"))
                suspect = " | evento suspeito: " + suspectSource + " (" + Elapsed(suspectTime, time) + " ms antes)";
            string apps = (d.IsInput ? " | usando o microfone: " : " | tocando som: ") + ActiveApps(d);
            Log("BLOQUEADO  " + d.Name + ": " + change + " | " + how + apps + suspect + repeats + " | contexto " + context);
            Notify("Mudança de volume bloqueada", d.Name + ": " + change + ". Voltei para " + Pct(d.Locked) + ".");
        }

        bool IsUserAction(Device d, int time, Guid context, out string how)
        {
            // Programa que já foi pego mexendo (mesmo contexto): nunca conta como você.
            if (context != Guid.Empty && blockedContexts.Contains(context))
            {
                how = "mesmo programa que já tinha sido bloqueado antes";
                return false;
            }

            // Teclas de volume e a rodinha no ícone de volume só mexem na saída que está em uso.
            bool isDefaultOutput = !d.IsInput && d.Id == DefaultOutputId();
            if (isDefaultOutput && userKeySource != null && Elapsed(userKeyTime, time) <= 1500)
            {
                how = "tecla de volume (" + userKeySource + ")";
                return true;
            }
            // Tecla simulada por programa ou sinal do Quadcast: nunca conta como você.
            if (!d.IsInput && suspectSource != null && Elapsed(suspectTime, time) <= 1000)
            {
                how = "veio de " + suspectSource + " (" + Elapsed(suspectTime, time) + " ms antes)";
                return false;
            }

            IntPtr foregroundWindow = Native.GetForegroundWindow();
            string foreground = WindowProcess(foregroundWindow);
            IntPtr under = IntPtr.Zero;
            Native.Point cursor;
            if (Native.GetCursorPos(out cursor)) under = Native.GetAncestor(Native.WindowFromPoint(cursor), Native.GA_ROOT);
            string underProcess = WindowProcess(under);
            string underClass = WindowClass(under);

            var input = new Native.LastInputInfo();
            input.Size = (uint)Marshal.SizeOf(typeof(Native.LastInputInfo));
            bool hasInput = Native.GetLastInputInfo(ref input);
            int inputAge = hasInput ? Elapsed((int)input.Time, time) : int.MaxValue;

            if (d.IsInput)
            {
                // O volume do microfone só muda pelas configurações de som: a janela precisa estar
                // em primeiro plano, com o mouse em cima dela, e você mexendo naquele momento.
                string ui = SettingsUi(foregroundWindow, foreground);
                if (inputAge <= 1500 && ui != null && SettingsUi(under, underProcess) != null)
                {
                    how = ui;
                    return true;
                }
            }
            else if (inputAge <= 2000)
            {
                // Rodinha do mouse no ícone de volume: o mouse precisa estar na área da bandeja
                // e a última ação precisa ter sido do mouse (digitar não conta).
                bool lastWasMouse = Elapsed(lastKeyboardTime, (int)input.Time) > 50;
                if (isDefaultOutput && (underClass == "Shell_TrayWnd" || underClass == "Shell_SecondaryTrayWnd") &&
                    inputAge <= 1000 && lastWasMouse && OverTrayArea(under, cursor))
                {
                    how = "ícone de volume da barra de tarefas";
                    return true;
                }
                string ui = OutputUi(foregroundWindow, foreground) ?? OutputUi(under, underProcess);
                if (ui != null)
                {
                    how = ui;
                    return true;
                }
            }
            how = "nenhuma ação sua | em primeiro plano: " + foreground + " | sob o mouse: " + underProcess;
            return false;
        }

        string DefaultOutputId()
        {
            IMMDevice device;
            if (enumerator == null || enumerator.GetDefaultAudioEndpoint(0, 1, out device) != 0 || device == null) return null;
            string id;
            device.GetId(out id);
            Marshal.ReleaseComObject(device);
            return id;
        }

        // Janelas onde dá para mudar o volume do microfone.
        static string SettingsUi(IntPtr window, string process)
        {
            switch (process)
            {
                case "systemsettings":
                    return "Configurações de som do Windows";
                case "rundll32":
                    return "painel de Som do Windows";
                case "gamebar":
                case "xboxgamebarwidgets":
                    return "Xbox Game Bar";
                case "applicationframehost":
                    // Esse processo hospeda vários apps; só vale se for a janela de Configurações.
                    string title = WindowTitle(window);
                    return title.IndexOf("Configura", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           title.IndexOf("Settings", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "Configurações de som do Windows" : null;
                default:
                    return null;
            }
        }

        // Janelas onde dá para mudar o volume das saídas.
        static string OutputUi(IntPtr window, string process)
        {
            if (process == "shellexperiencehost" || process == "shellhost") return "controle de volume do Windows (" + process + ")";
            if (process == "sndvol") return "Mixer de volume";
            return SettingsUi(window, process);
        }

        static string WindowTitle(IntPtr window)
        {
            if (window == IntPtr.Zero) return "";
            var text = new StringBuilder(256);
            Native.GetWindowText(window, text, text.Capacity);
            return text.ToString();
        }

        void Restore(Device d)
        {
            Guid context = OwnContext;
            d.Volume.SetMasterVolumeLevelScalar(d.Locked, ref context);
            bool mute;
            if (!d.IsInput && d.Volume.GetMute(out mute) == 0 && mute != d.LockedMute)
            {
                context = OwnContext;
                d.Volume.SetMute(d.LockedMute, ref context);
            }
            // O valor travado nunca é relido daqui: com tentativas seguidas, a releitura pode pegar
            // a próxima mudança do intruso e travar o valor dele no lugar do seu.
            d.Mismatches = 0;
        }

        void Accept(Device d, float volume, bool mute)
        {
            d.Locked = volume;
            d.LockedMute = mute;
            d.Mismatches = 0;
            Remember(d);
        }

        void Remember(Device d)
        {
            Level level;
            if (!saved.TryGetValue(d.Id, out level))
            {
                level = new Level();
                saved[d.Id] = level;
            }
            level.Volume = d.Locked;
            level.Mute = d.LockedMute;
            level.Name = d.Name;
            dirty = true;
        }

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

        // ------------------------------------------------------------ ícone e menu

        void BuildTray()
        {
            menu = new ContextMenuStrip();
            boldFont = new Font(menu.Font, FontStyle.Bold);
            menu.Opening += delegate(object sender, CancelEventArgs e) { FillMenu(); e.Cancel = false; };
            FillMenu();
            tray = new NotifyIcon();
            tray.ContextMenuStrip = menu;
            // Clique com o botão esquerdo também abre o menu.
            tray.MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                MethodInfo show = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
                if (show != null) show.Invoke(tray, null);
            };
            UpdateTray();
            tray.Visible = true;
        }

        void FillMenu()
        {
            menu.Items.Clear();
            var title = new ToolStripMenuItem(enabled ? "Trava de volume: LIGADA" : "Trava de volume: DESLIGADA");
            title.Enabled = false;
            menu.Items.Add(title);
            foreach (Device d in devices.Values)
            {
                var item = new ToolStripMenuItem("      " + d.Name + ": " + Pct(d.Locked) + (d.LockedMute ? " (silenciado)" : ""));
                item.Enabled = false;
                menu.Items.Add(item);
                foreach (TopologyControl c in d.Controls)
                {
                    if (c.Level == null) continue;
                    var sub = new ToolStripMenuItem("            › " + c.Source + ": " + PctDb(c, c.LockedDb));
                    sub.Enabled = false;
                    menu.Items.Add(sub);
                }
            }
            menu.Items.Add(new ToolStripSeparator());
            var toggle = new ToolStripMenuItem(enabled ? "Desligar a trava" : "Ligar a trava");
            toggle.Font = boldFont;
            toggle.Click += delegate { SetEnabled(!enabled); };
            menu.Items.Add(toggle);
            menu.Items.Add("Abrir o registro", null, delegate { OpenLog(); });
            var startup = new ToolStripMenuItem("Iniciar com o Windows");
            startup.Checked = AutoStart;
            startup.Click += delegate { AutoStart = !AutoStart; };
            menu.Items.Add(startup);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Sair", null, delegate { ExitThread(); });
        }

        void SetEnabled(bool value)
        {
            if (enabled == value) return;
            if (value)
            {
                // Ao religar, trava os níveis que estão valendo agora.
                foreach (Device d in devices.Values)
                {
                    float volume;
                    bool mute;
                    if (d.Volume.GetMasterVolumeLevelScalar(out volume) == 0 && d.Volume.GetMute(out mute) == 0)
                        Accept(d, volume, mute);
                    foreach (TopologyControl c in d.Controls)
                    {
                        float[] levels;
                        bool controlMute;
                        if (ReadControl(c, out levels, out controlMute)) AcceptControl(c, levels, controlMute);
                    }
                }
            }
            enabled = value;
            SaveConfig();
            if (value)
            {
                var levels = new List<string>();
                foreach (Device d in devices.Values) levels.Add(d.Name + " " + Pct(d.Locked) + (d.LockedMute ? " (silenciado)" : ""));
                Log("Trava LIGADA por você | travando: " + string.Join(", ", levels.ToArray()));
            }
            else
            {
                Log("Trava DESLIGADA por você");
            }
            UpdateTray();
        }

        void UpdateTray()
        {
            var old = tray.Icon;
            tray.Icon = MakeIcon(enabled);
            if (old != null) old.Dispose();
            tray.Text = enabled ? "Trava de volume: ligada" : "Trava de volume: desligada";
        }

        void Notify(string title, string text)
        {
            if (tray == null || (DateTime.Now - lastBalloon).TotalSeconds < 20) return;
            lastBalloon = DateTime.Now;
            tray.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);
        }

        void OpenLog()
        {
            try { Process.Start("notepad.exe", "\"" + logPath + "\""); } catch { }
        }

        bool AutoStart
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return key != null && key.GetValue(RunValue) != null;
            }
            set
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) key.SetValue(RunValue, "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue(RunValue, false);
                }
                Log(value ? "Início automático ativado" : "Início automático desativado");
            }
        }

        // Ligada: cadeado verde fechado. Desligada: cadeado cinza aberto.
        static Icon MakeIcon(bool locked)
        {
            Color color = locked ? Color.FromArgb(34, 160, 95) : Color.FromArgb(140, 140, 140);
            using (var bitmap = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var shackle = new Pen(color, 4f))
                    {
                        if (locked)
                        {
                            g.DrawArc(shackle, 9, 4, 14, 16, 180, 180);
                            g.DrawLine(shackle, 9, 12, 9, 17);
                            g.DrawLine(shackle, 23, 12, 23, 17);
                        }
                        else
                        {
                            // alça levantada e solta do lado direito
                            g.DrawArc(shackle, 9, 2, 14, 12, 180, 180);
                            g.DrawLine(shackle, 9, 8, 9, 17);
                        }
                    }
                    using (var body = new SolidBrush(color))
                    using (var path = RoundedRect(5, 15, 22, 16, 3))
                        g.FillPath(body, path);
                    g.FillEllipse(Brushes.White, 13, 19, 6, 6);
                    g.FillRectangle(Brushes.White, 15, 23, 2, 5);
                }
                IntPtr handle = bitmap.GetHicon();
                var icon = (Icon)Icon.FromHandle(handle).Clone();
                Native.DestroyIcon(handle);
                return icon;
            }
        }

        static GraphicsPath RoundedRect(int x, int y, int width, int height, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + width - d, y, d, d, 270, 90);
            path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
            path.AddArc(x, y + height - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ------------------------------------------------------------ arquivos

        public void Log(string text)
        {
            try
            {
                var info = new FileInfo(logPath);
                if (info.Exists && info.Length > 2 * 1024 * 1024)
                {
                    string old = Path.Combine(dir, "registro.anterior.txt");
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(logPath, old);
                }
                // Se o arquivo foi editado e ficou sem quebra de linha no fim, começa numa linha nova.
                string prefix = "";
                if (File.Exists(logPath))
                {
                    using (var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (stream.Length > 0)
                        {
                            stream.Seek(-1, SeekOrigin.End);
                            if (stream.ReadByte() != '\n') prefix = Environment.NewLine;
                        }
                    }
                }
                File.AppendAllText(logPath,
                    prefix + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + text + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch { }
        }

        void LoadLevels()
        {
            try
            {
                if (!File.Exists(levelsPath)) return;
                foreach (string line in File.ReadAllLines(levelsPath, Encoding.UTF8))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length < 3) continue;
                    var level = new Level();
                    level.Volume = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    level.Mute = parts[2] == "1";
                    level.Name = parts.Length > 3 ? parts[3] : "";
                    saved[parts[0]] = level;
                }
            }
            catch (Exception ex)
            {
                Log("Aviso: não consegui ler niveis.txt: " + ex.Message);
            }
        }

        void LoadControls()
        {
            try
            {
                if (!File.Exists(controlsPath)) return;
                foreach (string line in File.ReadAllLines(controlsPath, Encoding.UTF8))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length < 3) continue;
                    var control = new SavedControl();
                    if (parts[1].Length > 0)
                    {
                        string[] values = parts[1].Split(';');
                        control.Levels = new float[values.Length];
                        for (int i = 0; i < values.Length; i++)
                            control.Levels[i] = float.Parse(values[i], CultureInfo.InvariantCulture);
                    }
                    control.Mute = parts[2] == "1";
                    control.Name = parts.Length > 3 ? parts[3] : "";
                    savedControls[parts[0]] = control;
                }
            }
            catch (Exception ex)
            {
                Log("Aviso: não consegui ler controles.txt: " + ex.Message);
            }
        }

        void SaveControls()
        {
            try
            {
                var text = new StringBuilder();
                foreach (var pair in savedControls)
                {
                    var values = new List<string>();
                    if (pair.Value.Levels != null)
                        foreach (float value in pair.Value.Levels) values.Add(value.ToString("0.0000", CultureInfo.InvariantCulture));
                    text.Append(pair.Key).Append('\t')
                        .Append(string.Join(";", values.ToArray())).Append('\t')
                        .Append(pair.Value.Mute ? "1" : "0").Append('\t')
                        .Append(pair.Value.Name).Append("\r\n");
                }
                File.WriteAllText(controlsPath, text.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log("Aviso: não consegui salvar controles.txt: " + ex.Message);
            }
        }

        void LoadConfig()
        {
            try
            {
                if (File.Exists(configPath)) enabled = !File.ReadAllText(configPath).Contains("trava=desligada");
            }
            catch { }
        }

        void SaveConfig()
        {
            try { File.WriteAllText(configPath, enabled ? "trava=ligada" : "trava=desligada"); }
            catch (Exception ex) { Log("Aviso: não consegui salvar config.txt: " + ex.Message); }
        }

        void SaveLevels()
        {
            try
            {
                var text = new StringBuilder();
                foreach (var pair in saved)
                {
                    text.Append(pair.Key).Append('\t')
                        .Append(pair.Value.Volume.ToString("0.0000", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(pair.Value.Mute ? "1" : "0").Append('\t')
                        .Append(pair.Value.Name).Append("\r\n");
                }
                File.WriteAllText(levelsPath, text.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log("Aviso: não consegui salvar niveis.txt: " + ex.Message);
            }
            SaveControls();
        }

        protected override void ExitThreadCore()
        {
            timer.Stop();
            foreach (string id in new List<string>(devices.Keys)) Detach(id, false);
            if (dirty) SaveLevels();
            Log("==== Trava de volume encerrada ====");
            tray.Visible = false;
            tray.Dispose();
            if (boldFont != null) boldFont.Dispose();
            window.Dispose();
            base.ExitThreadCore();
        }
    }
}
