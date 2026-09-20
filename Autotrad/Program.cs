using System;
using System.Text;
using System.Windows.Forms;

namespace Autotrad
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            ApplicationConfiguration.Initialize();

            // ?? Handler global d'exceptions non gérées
            Application.ThreadException += (s, e) =>
            {
                LogCrash("ThreadException", e.Exception);

                MessageBox.Show(
                    "Une erreur inattendue est survenue :\n\n" + e.Exception.Message,
                    "Autotrad - Erreur",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                LogCrash("UnhandledException", e.ExceptionObject as Exception);
            };

            Application.Run(new FormMain());
        }

        // ============================================================
        //  Log de crash (fichier à côté de l'exe)
        // ============================================================
        private static void LogCrash(string source, Exception? ex)
        {
            try
            {
                string logPath = System.IO.Path.Combine(
                    AppContext.BaseDirectory,
                    "autotrad.crash.log"
                );

                System.IO.File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}:\n{ex}\n\n");
            }
            catch { }
        }
    }
}