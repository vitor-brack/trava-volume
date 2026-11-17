using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TravaVolume
{
    public partial class Guard
    {
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
    }
}
