using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TravaVolume
{
    public partial class Guard : ApplicationContext
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
    }
}
