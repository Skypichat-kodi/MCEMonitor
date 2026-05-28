using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

public static class SystemDiagnostics
{
    public static string GetInfo()
    {
        var sb = new StringBuilder();

        try
        {
            sb.AppendLine($"Machine : {Environment.MachineName}");
            sb.AppendLine($"Utilisateur : {Environment.UserName}");
            sb.AppendLine($"OS : {Environment.OSVersion}");
            sb.AppendLine($"Uptime : {TimeSpan.FromMilliseconds(Environment.TickCount64)}");

            // ------------------------------------------------------------
            // CPU usage (méthode moderne sans PerformanceCounter)
            // ------------------------------------------------------------
            try
            {
                var cpuUsage = GetCpuUsage();
                sb.AppendLine($"CPU : {cpuUsage:F1}%");
            }
            catch
            {
                sb.AppendLine("CPU : impossible à lire");
            }

            // ------------------------------------------------------------
            // RAM usage via GlobalMemoryStatusEx (API Windows)
            // ------------------------------------------------------------
            try
            {
                MEMORYSTATUSEX mem = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(mem))
                {
                    ulong total = mem.ullTotalPhys;
                    ulong free = mem.ullAvailPhys;

                    sb.AppendLine($"RAM totale : {total / 1024 / 1024} MB");
                    sb.AppendLine($"RAM libre : {free / 1024 / 1024} MB");
                    sb.AppendLine($"RAM utilisée : {(total - free) / 1024 / 1024} MB");
                }
                else
                {
                    sb.AppendLine("RAM : impossible à lire");
                }
            }
            catch
            {
                sb.AppendLine("RAM : impossible à lire");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("ERREUR SystemDiagnostics : " + ex.Message);
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------
    // CPU usage via Process.TotalProcessorTime (compatible .NET 8)
    // ------------------------------------------------------------
    private static double GetCpuUsage()
    {
        var p = Process.GetCurrentProcess();
        var startCpu = p.TotalProcessorTime;
        var startTime = DateTime.UtcNow;

        System.Threading.Thread.Sleep(200);

        var endCpu = p.TotalProcessorTime;
        var endTime = DateTime.UtcNow;

        double cpuUsedMs = (endCpu - startCpu).TotalMilliseconds;
        double totalMs = (endTime - startTime).TotalMilliseconds * Environment.ProcessorCount;

        return (cpuUsedMs / totalMs) * 100.0;
    }

    // ------------------------------------------------------------
    // RAM via GlobalMemoryStatusEx
    // ------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
}

