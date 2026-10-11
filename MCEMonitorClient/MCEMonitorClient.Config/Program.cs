using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Interop;
using MCEMonitor.Languages;

namespace MCEMonitorClient.Config
{
    internal static class Program
    {
        private static Mutex? _mutex;

        private const string MutexName = "Global\\MCEMonitorClient_Config";
        private const string PipeName  = "MCEMonitorClient_Config_Activate";

        [STAThread]
        static void Main(string[] args)
        {
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);

            if (!createdNew)
            {
                // Une instance tourne déjà ? lui demander de venir au premier plan
                SignalExistingInstance();
                return;
            }

            // Enregistre le provider d'encodage (pour Encoding.GetEncoding(850))
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // ============================================================
            //  GESTION DE LA LANGUE (argument > auto-détection Windows)
            // ============================================================

            string selectedLang = null;

            if (args.Contains("-EN", StringComparer.OrdinalIgnoreCase))
                selectedLang = "en-GB";
            else if (args.Contains("-FR", StringComparer.OrdinalIgnoreCase))
                selectedLang = "fr-FR";

            if (selectedLang == null)
            {
                string[] supportedLanguages = { "fr-FR", "en-GB" };
                string windowsLang = System.Globalization.CultureInfo.InstalledUICulture.Name;

                selectedLang = supportedLanguages.Contains(windowsLang)
                    ? windowsLang
                    : "en-GB";
            }

            System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(selectedLang);
            System.Threading.Thread.CurrentThread.CurrentCulture   = new System.Globalization.CultureInfo(selectedLang);

            LanguageManager.Load(selectedLang);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Première instance ? démarre le listener
            StartActivationListener();

            Application.Run(new MainForm());
        }

        // ============================================================
        //  Signale à l'instance existante de venir au premier plan
        // ============================================================
        private static void SignalExistingInstance()
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    ".", PipeName, PipeDirection.Out);

                client.Connect(1500);

                using var writer = new StreamWriter(client);
                writer.WriteLine("ACTIVATE");
                writer.Flush();
            }
            catch { }
        }

        // ============================================================
        //  Thread qui écoute les demandes d'activation
        // ============================================================
        private static void StartActivationListener()
        {
            var thread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(
                            PipeName,
                            PipeDirection.In,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.None);

                        server.WaitForConnection();

                        using var reader = new StreamReader(server);
                        string? cmd = reader.ReadLine();

                        if (cmd == "ACTIVATE")
                        {
                            // Remet la fenêtre principale au premier plan
                            var mainForm = Application.OpenForms
                                .Cast<Form>()
                                .FirstOrDefault(f => f is MainForm);

                            if (mainForm != null)
                            {
                                mainForm.Invoke(new Action(() =>
                                {
                                    if (mainForm.WindowState == FormWindowState.Minimized)
                                        mainForm.WindowState = FormWindowState.Normal;

                                    mainForm.Show();
                                    mainForm.Activate();
                                    mainForm.BringToFront();
                                }));
                            }
                        }
                    }
                    catch
                    {
                        // Continue la boucle
                    }
                }
            })
            {
                IsBackground = true,
                Name = "ConfigActivationListener"
            };

            thread.Start();
        }
    }
}