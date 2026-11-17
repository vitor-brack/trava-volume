// Trava de volume: mantém o volume das saídas de áudio e dos microfones no nível que você escolheu.
// Mudanças feitas por você (teclas de volume, ícone de volume da barra de tarefas,
// Configurações rápidas, Configurações de som, Mixer de volume) são aceitas; qualquer outra é
// desfeita na hora e anotada em %LOCALAPPDATA%\TravaVolume\registro.txt.
// Também trava os controles extras da aba Níveis (ex.: o retorno do microfone no fone).
// O mudo dos microfones nunca é travado: desfazer um "mudo" poderia reativar o microfone sem você saber.

using System;
using System.Threading;
using System.Windows.Forms;

namespace TravaVolume
{
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
}
