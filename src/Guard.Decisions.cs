using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TravaVolume
{
    public partial class Guard
    {
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
    }
}
