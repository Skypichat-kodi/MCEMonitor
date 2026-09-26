using System;
using System.IO;
using System.Text;

namespace SystemMonitor.Service
{
    /// <summary>
    /// Analyse les fichiers de dump Windows.
    /// Supporte les DUMP_HEADER64 (kernel dumps, "PAGEDU64") et les MINIDUMP_HEADER (minidumps utilisateur, "MDMP").
    /// </summary>
    public static class MinidumpParser
    {
        private const uint MDMP_SIGNATURE = 0x504D444D;   // "MDMP"
        private const uint PAGE_SIGNATURE = 0x45474150;   // "PAGE"
        private const uint DU64_SIGNATURE = 0x34365544;   // "DU64"

        // ============================================================
        //  Point d'entrée public
        // ============================================================
        public static FaultInfo? Analyze(string dumpPath)
        {
            try
            {
                if (!File.Exists(dumpPath))
                    return null;

                byte[] data = File.ReadAllBytes(dumpPath);
                return AnalyzeFromBytes(data);
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur Analyze dump : " + ex.Message);
                return null;
            }
        }

        private static FaultInfo? AnalyzeFromBytes(byte[] data)
        {
            if (data.Length < 64)
                return null;

            // Identifier le format
            uint sig1 = BitConverter.ToUInt32(data, 0);
            uint sig2 = BitConverter.ToUInt32(data, 4);

            CoreLog.Write($"Minidump : taille={data.Length} bytes, sig1=0x{sig1:X8}, sig2=0x{sig2:X8}");

            // ----- Format kernel : PAGEDU64 -----
            if (sig1 == PAGE_SIGNATURE && sig2 == DU64_SIGNATURE)
            {
                CoreLog.Write("Minidump : format kernel détecté (PAGEDU64)");
                return ParseKernelDump(data);
            }

            // ----- Format utilisateur : MDMP -----
            if (sig1 == MDMP_SIGNATURE)
            {
                CoreLog.Write("Minidump : format utilisateur détecté (MDMP)");
                return ParseUserMinidump(data, 0);
            }

            // Chercher MDMP plus loin
            long mdmpOffset = FindMdmpSignature(data);
            if (mdmpOffset >= 0)
            {
                CoreLog.Write($"Minidump : MDMP trouvé à l'offset 0x{mdmpOffset:X}");
                return ParseUserMinidump(data, mdmpOffset);
            }

            CoreLog.Write("Minidump : format non reconnu");
            return null;
        }

        // ============================================================
        //  Parser un kernel dump (PAGEDU64)
        // ============================================================
        private static FaultInfo? ParseKernelDump(byte[] data)
        {
            try
            {
                // DUMP_HEADER64 layout (Windows 10/11) :
                //   0x00 : Signature "PAGE"
                //   0x04 : ValidDump "DU64"
                //   0x08 : MajorVersion
                //   0x0C : MinorVersion
                //   0x10 : DirectoryTableBase (8)
                //   0x18 : PfnDataBase (8)
                //   0x20 : PsLoadedModuleList (8)
                //   0x28 : PsActiveProcessHead (8)
                //   0x30 : MachineImageType (4)
                //   0x34 : NumberProcessors (4)
                //   0x38 : BugCheckCode (4)      ? décalé selon la version
                //   0x3C : (padding)
                //   0x40 : BugCheckParameter1 (8)
                //   0x48 : BugCheckParameter2 (8)
                //   0x50 : BugCheckParameter3 (8)
                //   0x58 : BugCheckParameter4 (8)

                uint major = BitConverter.ToUInt32(data, 0x08);
                uint minor = BitConverter.ToUInt32(data, 0x0C);

                CoreLog.Write($"Minidump kernel : version {major}.{minor} (build {minor})");

                // Essayer plusieurs offsets pour le BugCheckCode
                uint bugCheckCode = 0;
                int codeOffset = -1;

                // Tester offset 0x38 puis 0x3C
                uint candidate1 = BitConverter.ToUInt32(data, 0x38);
                uint candidate2 = BitConverter.ToUInt32(data, 0x3C);

                // Un bugcheck code valide est < 0x200 en général, ou >= 0xC0000000 pour les spéciaux
                bool isValid1 = candidate1 > 0 && candidate1 < 0x1000;
                bool isValid2 = candidate2 > 0 && candidate2 < 0x1000;

                CoreLog.Write($"Minidump : candidats bugcheck : 0x{candidate1:X} (0x38) / 0x{candidate2:X} (0x3C)");

                // On préfère celui qui n'est pas 0
                if (isValid2)
                {
                    bugCheckCode = candidate2;
                    codeOffset = 0x3C;
                }
                else if (isValid1)
                {
                    bugCheckCode = candidate1;
                    codeOffset = 0x38;
                }

                if (bugCheckCode == 0)
                {
                    CoreLog.Write("Minidump : bugcheck code introuvable dans le header");
                    return null;
                }

                CoreLog.Write($"Minidump : bugcheck=0x{bugCheckCode:X} (offset 0x{codeOffset:X})");

                // Paramètres : après le code, alignés à 8 bytes
                int paramOffset = codeOffset + 4;
                // Aligner sur 8 bytes
                if (paramOffset % 8 != 0)
                    paramOffset += 4;

                ulong p1 = BitConverter.ToUInt64(data, paramOffset);
                ulong p2 = BitConverter.ToUInt64(data, paramOffset + 8);
                ulong p3 = BitConverter.ToUInt64(data, paramOffset + 16);
                ulong p4 = BitConverter.ToUInt64(data, paramOffset + 24);

                CoreLog.Write($"Minidump : params 0x{p1:X16}, 0x{p2:X16}, 0x{p3:X16}, 0x{p4:X16}");

                return new FaultInfo
                {
                    FaultAddress = 0,
                    FaultyModulePath = "",
                    FaultyModuleName = "Kernel dump - voir WinDbg pour identifier le pilote",
                    ModuleBase = 0,
                    ModuleCount = 0,
                    IsKernelDump = true,
                    BugCheckCode = $"0x{bugCheckCode:X8}",
                    BugCheckParameters = $"0x{p1:X16}, 0x{p2:X16}, 0x{p3:X16}, 0x{p4:X16}"
                };
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur ParseKernelDump : " + ex.Message);
                return null;
            }
        }

        // ============================================================
        //  Parser un minidump utilisateur (MDMP)
        // ============================================================
        private static FaultInfo? ParseUserMinidump(byte[] data, long baseOffset)
        {
            CoreLog.Write("Minidump : parsing utilisateur non supporté pour ce cas");
            return new FaultInfo
            {
                FaultyModuleName = "Minidump utilisateur - voir WinDbg",
                IsKernelDump = false
            };
        }

        // ============================================================
        //  Recherche de la signature MDMP dans le fichier
        // ============================================================
        private static long FindMdmpSignature(byte[] data)
        {
            int limit = data.Length - 4;

            for (int i = 0; i < limit; i++)
            {
                if (data[i] == 0x4D && data[i + 1] == 0x44 &&
                    data[i + 2] == 0x4D && data[i + 3] == 0x50)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    // ============================================================
    //  Résultat du parsing
    // ============================================================
    public class FaultInfo
    {
        public ulong FaultAddress { get; set; }
        public string FaultyModulePath { get; set; } = "";
        public string FaultyModuleName { get; set; } = "";
        public ulong ModuleBase { get; set; }
        public int ModuleCount { get; set; }
        public bool IsKernelDump { get; set; }                  // ? NOUVEAU
        public string BugCheckCode { get; set; } = "";          // ? NOUVEAU
        public string BugCheckParameters { get; set; } = "";    // ? NOUVEAU
    }
}