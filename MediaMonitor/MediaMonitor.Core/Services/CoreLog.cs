using System;
using System.IO;

namespace MediaMonitor.Core.Services
{
    public static class CoreLog
    {
        // ?? Le service fournit cette fonction :
        //    return ServiceIpcServer.ServiceLoggingEnabled;
        public static Func<bool>? IsLoggingEnabled;

        public static void Write(string message)
        {
            try
            {
                // ?? Si le service dit "log OFF", on ne log rien
                if (IsLoggingEnabled != null && !IsLoggingEnabled())
                    return;

                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "Logs"
                );

                Directory.CreateDirectory(folder);

                string file = Path.Combine(folder, "MediaMonitor.Service.log");

                File.AppendAllText(
                    file,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}"
                );
            }
            catch
            {
                // Jamais casser le moteur pour un log
            }
        }
    }
}

