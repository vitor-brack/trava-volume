using System.Windows.Forms;

namespace TravaVolume
{
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
}
