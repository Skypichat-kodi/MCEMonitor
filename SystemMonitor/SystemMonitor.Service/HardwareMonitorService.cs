using System;
using System.Collections.Generic;
using System.Management;
using LibreHardwareMonitor.Hardware;

namespace SystemMonitor.Service
{
    /// <summary>
    /// Collecte les informations matérielles via LibreHardwareMonitorLib + WMI.
    /// </summary>
    public class HardwareMonitorService : IDisposable
    {
        private readonly Computer _computer;
        private readonly UpdateVisitor _updateVisitor;
        private bool _opened;

        public HardwareMonitorService()
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
                IsNetworkEnabled = true,
                IsStorageEnabled = true
            };

            _updateVisitor = new UpdateVisitor();

            try
            {
                _computer.Open();
                _opened = true;
                CoreLog.Write("HardwareMonitorService : LibreHardwareMonitorLib ouvert");
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur ouverture LHM : " + ex.Message);
            }
        }

        /// <summary>
        /// Rafraîchit tous les capteurs et retourne un snapshot.
        /// </summary>
        public SystemSnapshot GetSnapshot()
        {
            var snapshot = new SystemSnapshot
            {
                Timestamp = DateTime.Now,
                Cpu = new CpuInfo(),
                Ram = new RamInfo()
            };

            try
            {
                if (_opened)
                {
                    _computer.Accept(_updateVisitor);

                    foreach (var hardware in _computer.Hardware)
                    {
                        switch (hardware.HardwareType)
                        {
                            case HardwareType.Cpu:
                                FillCpu(snapshot.Cpu, hardware);
                                break;

                            case HardwareType.GpuNvidia:
                            case HardwareType.GpuAmd:
                            case HardwareType.GpuIntel:
                                var gpu = new GpuInfo();
                                FillGpu(gpu, hardware);
                                snapshot.Gpus.Add(gpu);
                                break;

                            case HardwareType.Network:
                                var net = new NetworkInfo();
                                FillNetwork(net, hardware);
                                snapshot.Networks.Add(net);
                                break;
                        }
                    }
                }

                // Compléments WMI (plus fiables pour certains cas)
                FillRamFromWmi(snapshot.Ram);
                FillCpuNameFromWmi(snapshot.Cpu);
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur GetSnapshot : " + ex.Message);
            }

            return snapshot;
        }

        // ============================================================
        //  CPU
        // ============================================================
        private void FillCpu(CpuInfo cpu, IHardware hw)
        {
            cpu.Name = hw.Name;
            cpu.Cores.Clear();

            var cores = new Dictionary<string, double>();

            foreach (var sensor in hw.Sensors)
            {
                switch (sensor.SensorType)
                {
                    case SensorType.Load when sensor.Name == "CPU Total":
                        cpu.UsagePercent = sensor.Value ?? 0;
                        break;

                    case SensorType.Temperature when sensor.Name.Contains("Package"):
                    case SensorType.Temperature when sensor.Name.Contains("Core"):
                        if (cpu.Temperature == null || sensor.Value > cpu.Temperature)
                            cpu.Temperature = sensor.Value;
                        break;

                    case SensorType.Clock when sensor.Name.Contains("Core"):
                        if (cpu.FrequencyMHz == null || sensor.Value > cpu.FrequencyMHz)
                            cpu.FrequencyMHz = sensor.Value;
                        break;

                    // ? NOUVEAU : charge par cœur
                    case SensorType.Load when System.Text.RegularExpressions.Regex.IsMatch(
                        sensor.Name, @"^CPU Core #\d+$"):
                        cores[sensor.Name] = sensor.Value ?? 0;
                        break;
                }
            }

            // Tri des cœurs par numéro réel (#1, #2, ..., #10, #11)
            var sortedCores = cores
                .Select(kv => new
                {
                    Key = kv.Key,
                    Value = kv.Value,
                    Number = ExtractCoreNumber(kv.Key)
                })
                .OrderBy(x => x.Number);

            foreach (var item in sortedCores)
            {
                cpu.Cores.Add(new CpuCoreInfo
                {
                    Name = $"Core {item.Number:D2}",   // 01, 02, ..., 10, 11
                    UsagePercent = item.Value
                });
            }
        }

        /// <summary>
        /// Extrait le numéro depuis "CPU Core #12" ? 12
        /// </summary>
        private static int ExtractCoreNumber(string name)
        {
            var match = System.Text.RegularExpressions.Regex.Match(name, @"#(\d+)");
            return match.Success && int.TryParse(match.Groups[1].Value, out int n) ? n : 0;
        }

        private void FillCpuNameFromWmi(CpuInfo cpu)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, MaxClockSpeed FROM Win32_Processor");

                foreach (ManagementObject m in searcher.Get())
                {
                    if (string.IsNullOrEmpty(cpu.Name))
                        cpu.Name = m["Name"]?.ToString() ?? "N/A";

                    cpu.MaxFrequencyMHz = Convert.ToDouble(m["MaxClockSpeed"] ?? 0);
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur FillCpuNameFromWmi : " + ex.Message);
            }
        }

        // ============================================================
        //  GPU
        // ============================================================
        private void FillGpu(GpuInfo gpu, IHardware hw)
        {
            gpu.Name = hw.Name;

            foreach (var sensor in hw.Sensors)
            {
                switch (sensor.SensorType)
                {
                    case SensorType.Load when sensor.Name == "GPU Core":
                        gpu.UsagePercent = sensor.Value ?? 0;
                        break;

                    case SensorType.Temperature when sensor.Name.Contains("Core"):
                        gpu.Temperature = sensor.Value;
                        break;

                    case SensorType.SmallData when sensor.Name.Contains("Memory Used"):
                        gpu.VramUsedMB = sensor.Value ?? 0;
                        break;

                    case SensorType.SmallData when sensor.Name.Contains("Memory Total"):
                        gpu.VramTotalMB = sensor.Value ?? 0;
                        break;
                }
            }
        }

        // ============================================================
        //  RAM (via WMI — plus fiable que LHM)
        // ============================================================
        private void FillRamFromWmi(RamInfo ram)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");

                foreach (ManagementObject m in searcher.Get())
                {
                    double totalKB = Convert.ToDouble(m["TotalVisibleMemorySize"]);
                    double freeKB = Convert.ToDouble(m["FreePhysicalMemory"]);

                    ram.TotalGB = totalKB / 1024.0 / 1024.0;
                    ram.FreeGB = freeKB / 1024.0 / 1024.0;
                    ram.UsedGB = ram.TotalGB - ram.FreeGB;
                    ram.UsagePercent = ram.TotalGB > 0
                        ? (ram.UsedGB / ram.TotalGB) * 100
                        : 0;
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur FillRamFromWmi : " + ex.Message);
            }
        }

        // ============================================================
        //  Réseau
        // ============================================================
        private void FillNetwork(NetworkInfo net, IHardware hw)
        {
            net.Name = hw.Name;

            foreach (var sensor in hw.Sensors)
            {
                switch (sensor.SensorType)
                {
                    case SensorType.Throughput when sensor.Name.Contains("Download"):
                        net.DownloadKBps = sensor.Value ?? 0;
                        break;

                    case SensorType.Throughput when sensor.Name.Contains("Upload"):
                        net.UploadKBps = sensor.Value ?? 0;
                        break;
                }
            }
        }

        public void Dispose()
        {
            try
            {
                if (_opened)
                    _computer?.Close();
            }
            catch { }
        }
    }

    /// <summary>
    /// Visiteur obligatoire pour rafraîchir les capteurs LHM.
    /// </summary>
    public class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer)
        {
            computer.Traverse(this);
        }

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware)
                sub.Accept(this);
        }

        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}