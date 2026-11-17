using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;

namespace TravaVolume
{
    public partial class Guard
    {
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
    }
}
