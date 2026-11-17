using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TravaVolume
{
    public partial class Guard
    {
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
