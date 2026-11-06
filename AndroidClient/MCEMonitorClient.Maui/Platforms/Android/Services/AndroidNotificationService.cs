using Android.App;
using Android.Content;
using Android.Media;
using Android.OS;
using Android.Graphics;
using AndroidX.Core.App;
using MCEMonitorClient.Shared.Models;
using System.Collections.Generic;

namespace MCEMonitorClient.Maui.Platforms.Android.Services
{
    public static class AndroidNotificationService
    {
        public const string ChannelCritical   = "mcem_critical";
        public const string ChannelWarning    = "mcem_warning";
        public const string ChannelDisconnect = "mcem_disconnect";   // ? nouveau
        public const string ChannelInfo       = "mcem_info";

        private const string ServiceChannel = "mcem_service";
        private static int _nextId = 1000;

        public static void CreateChannels(Context context)
        {
            // L'analyseur CA1416 ne reconnaît que OperatingSystem.IsAndroidVersionAtLeast()
            // Build.VERSION.SdkInt < BuildVersionCodes.O ne satisfait pas le checker.
            if (!OperatingSystem.IsAndroidVersionAtLeast(26))
                return;

            var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;

            // ---------- Canal CRITICAL : alarm.wav ----------
            var criticalSound = global::Android.Net.Uri.Parse(
                $"android.resource://{context.PackageName}/{Resource.Raw.alarm}");
            var critical = new NotificationChannel(ChannelCritical, "Alertes critiques",
                NotificationImportance.High)
            {
                Description = "Alertes serveur critiques"
            };

            // Le binding .NET Android marque AudioAttributes.Builder() comme nullable
            // (annotation exagérée, le constructeur ne retourne jamais null).
            #pragma warning disable CS8602
            var criticalAttrs = new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Notification).Build();
            #pragma warning restore CS8602
            critical.SetSound(criticalSound, criticalAttrs);
            critical.EnableVibration(true);

            // ---------- Canal WARNING : warning.wav ----------
            var warningSound = global::Android.Net.Uri.Parse(
                $"android.resource://{context.PackageName}/{Resource.Raw.warning}");
            var warning = new NotificationChannel(ChannelWarning, "Avertissements",
                NotificationImportance.Default)
            {
                Description = "Avertissements serveur"
            };

            #pragma warning disable CS8602
            var warningAttrs = new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Notification).Build();
            #pragma warning restore CS8602
            warning.SetSound(warningSound, warningAttrs);

            // ---------- Canal DISCONNECT : son système par défaut ----------
            // IMPORTANCE_DEFAULT + aucun SetSound() ? Android utilise le son
            // de notification système par défaut. Non urgent, mais audible.
            var disconnect = new NotificationChannel(
                ChannelDisconnect,
                "Déconnexions serveur",
                NotificationImportance.Default)
            {
                Description = "Notifications de déconnexion / reconnexion des serveurs"
            };
            // ? On n'appelle PAS SetSound() ici ? son système par défaut

            // ---------- Canal INFO : silencieux (média) ----------
            var info = new NotificationChannel(ChannelInfo, "Informations",
                NotificationImportance.Low)
            {
                Description = "Notifications informatives"
            };
            info.SetSound(null, null);

            // ---------- Canal SERVICE : silencieux (foreground) ----------
            var service = new NotificationChannel(ServiceChannel, "Service de surveillance",
                NotificationImportance.Low)
            {
                Description = "Notification permanente du service"
            };
            service.SetSound(null, null);

            manager.CreateNotificationChannel(critical);
            manager.CreateNotificationChannel(warning);
            manager.CreateNotificationChannel(disconnect);   // ? nouveau
            manager.CreateNotificationChannel(info);
            manager.CreateNotificationChannel(service);
        }

        public static void ShowAlert(Context context, PushAlert alert)
        {
            // Routage par canal selon l'état
            string channel = alert.Status switch
            {
                "critical" => ChannelCritical,
                "warning"  => ChannelWarning,
                "offline"  => ChannelDisconnect,   // ? nouveau : son système par défaut
                "ok"       => ChannelDisconnect,   // ? retour à la normale, idem
                _          => ChannelInfo
            };

            int priority = alert.Status switch
            {
                "critical" => (int)NotificationPriority.High,
                "warning"  => (int)NotificationPriority.Default,
                "offline"  => (int)NotificationPriority.Default,
                "ok"       => (int)NotificationPriority.Default,
                _          => (int)NotificationPriority.Low
            };

            string title;

            // 1. Menace neutralisée (NetMonitor)
            if (alert.Status == "ok" && alert.PreviousStatus != "ok" && alert.LastBlock != null &&
                (DateTime.UtcNow - alert.LastBlock.Timestamp.ToUniversalTime()).TotalMinutes < 5)
            {
                title = $"{alert.ServerName} - Menace neutralisée";
            }
            // 2. Résolution détaillée
            else if (alert.Status == "ok" && alert.PreviousStatus != "ok" &&
                     alert.ResolvedProblems != null && alert.ResolvedProblems.Count > 0)
            {
                int realCount = alert.ResolvedProblems.Count(p => {
                    var k = Classify(p);
                    return k != ProblemKind.Unknown && k != ProblemKind.Bsod;
                });

                title = realCount <= 1
                    ? $"{alert.ServerName} - Retour à la normale"
                    : $"{alert.ServerName} - {realCount} alertes résolues";
            }
            // 3. Nouveaux problèmes sans changement de statut
            else if (alert.NewProblems != null && alert.NewProblems.Count > 0
                     && alert.Status == alert.PreviousStatus)
            {
                // On liste les catégories concernées
                var kindLabels = alert.NewProblems
                    .Select(p => Classify(p))
                    .Where(k => k != ProblemKind.Unknown && k != ProblemKind.Bsod)
                    .Distinct()
                    .Select(KindLabel)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();

                string detail = kindLabels.Count > 0
                    ? string.Join(", ", kindLabels)
                    : $"{alert.NewProblems.Count} nouveau(x)";

                title = $"{alert.ServerName} - Nouveau : {detail}";
            }
            // 4. Cas standard
            else
            {
                // Compte les ToCheck pour adapter le titre
                int toCheckCount = alert.Problems?.Count(p =>
                    string.Equals(p.Severity, "tocheck", StringComparison.OrdinalIgnoreCase)) ?? 0;

                if (toCheckCount > 0)
                {
                    title = $"{alert.ServerName} - Alerte à vérifier ({toCheckCount})";
                }
                else
                {
                    title = alert.Status switch
                    {
                        "critical" => $"{alert.ServerName} - CRITIQUE",
                        "warning"  => $"{alert.ServerName} - Alerte",
                        "offline"  => $"{alert.ServerName} - Hors ligne",
                        "ok"       => $"{alert.ServerName} - OK",
                        _          => alert.ServerName
                    };
                }
            }

            string message = BuildAlertMessage(alert);

            var intent = new Intent(Intent.ActionView,
                global::Android.Net.Uri.Parse(alert.BaseUrl));
            var pending = PendingIntent.GetActivity(
                context, 0, intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            var builder = new NotificationCompat.Builder(context, channel)
                .SetContentTitle(title)
                .SetContentText(message)
                .SetStyle(new NotificationCompat.BigTextStyle().BigText(message))
                .SetSmallIcon(Resource.Drawable.ic_stat_notify)
                .SetPriority(priority)
                .SetContentIntent(pending)
                .SetAutoCancel(true);

            string iconName = GetAlertIconName(alert);
            var largeIcon = LoadLargeIcon(context, iconName);
            if (largeIcon != null)
                builder.SetLargeIcon(largeIcon);

            var manager = NotificationManagerCompat.From(context);
            manager.Notify(_nextId++, builder.Build());
        }

        public static void ShowMedia(Context context, PushMedia media)
        {
            // Ignore les arrêts de lecture audio (trop fréquent, pas utile)
            if (media.EventType == "stopped" && IsAudioType(media.MediaType))
            {
                return;
            }

            bool isRecording = IsRecordingType(media.MediaType);
            bool isStarted   = media.EventType == "started";

            string title;
            string info = "";

            if (isRecording)
            {
                // ─── Enregistrement DVBViewer ───
                title = isStarted
                    ? $"{media.ServerName} - Enregistrement démarré"
                    : $"{media.ServerName} - Enregistrement terminé";

                if (!string.IsNullOrEmpty(media.Title))
                    info += media.Title;
                else
                    info += "(enregistrement)";

                if (!string.IsNullOrEmpty(media.Client))
                    info += $"\nDepuis : {media.Client}";

                if (media.Saison > 0 || media.Episode > 0)
                    info += $"  ({media.Saison:00}x{media.Episode:00})";
            }
            else
            {
                // ─── Lecture classique (audio/video/tv/image) ───
                title = isStarted
                    ? $"{media.ServerName} - Lecture en cours"
                    : $"{media.ServerName} - Lecture terminée";

                if (!string.IsNullOrEmpty(media.Client))
                    info += $"Depuis : {media.Client}\n";
                info += string.IsNullOrEmpty(media.Title) ? media.MediaType : media.Title;
                if (media.Saison > 0 || media.Episode > 0)
                    info += $"  ({media.Saison:00}x{media.Episode:00})";
            }

            var intent = new Intent(Intent.ActionView,
                global::Android.Net.Uri.Parse(media.BaseUrl));
            var pending = PendingIntent.GetActivity(
                context, 0, intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            var builder = new NotificationCompat.Builder(context, ChannelInfo)
                .SetContentTitle(title)
                .SetContentText(info)
                .SetStyle(new NotificationCompat.BigTextStyle().BigText(info))
                .SetSmallIcon(Resource.Drawable.ic_stat_notify)
                .SetPriority((int)NotificationPriority.Low)
                .SetContentIntent(pending)
                .SetAutoCancel(true);

            string iconName = GetMediaIconName(media.MediaType, media.EventType);
            var largeIcon = LoadLargeIcon(context, iconName);
            if (largeIcon != null)
                builder.SetLargeIcon(largeIcon);

            var manager = NotificationManagerCompat.From(context);
            manager.Notify(_nextId++, builder.Build());
        }

        private static string BuildAlertMessage(PushAlert alert)
        {
            // ---- Menace neutralisée (NetMonitor) ----
            if (alert.Status == "ok" && alert.PreviousStatus != "ok" && alert.LastBlock != null &&
                (DateTime.UtcNow - alert.LastBlock.Timestamp.ToUniversalTime()).TotalMinutes < 5)
            {
                string mode = alert.LastBlock.Automatic ? "automatiquement" : "manuellement";
                string msg = $"L'IP {alert.LastBlock.Ip} a été bloquée {mode} par le pare-feu";
                if (!string.IsNullOrWhiteSpace(alert.LastBlock.Detector))
                    msg += $" (détecteur : {alert.LastBlock.Detector})";
                return msg;
            }

            // ---- Résolution détaillée ----
            if (alert.Status == "ok" && alert.PreviousStatus != "ok" &&
                alert.ResolvedProblems != null && alert.ResolvedProblems.Count > 0)
            {
                var realResolved = alert.ResolvedProblems
                    .Where(p => {
                        var k = Classify(p);
                        return k != ProblemKind.Unknown && k != ProblemKind.Bsod;
                    })
                    .ToList();

                if (realResolved.Count == 0)
                    return "Le serveur est de nouveau joignable";

                // Résumé par catégorie
                var byKind = realResolved
                    .Select(p => Classify(p))
                    .GroupBy(k => k)
                    .OrderByDescending(g => IsDiskKind(g.Key))
                    .ThenByDescending(g => IsCpuKind(g.Key))
                    .ToList();

                var parts = new List<string>();
                foreach (var g in byKind)
                {
                    string label = KindLabel(g.Key);
                    if (!string.IsNullOrEmpty(label))
                        parts.Add($"{label} ?");
                }

                string summary = string.Join(" • ", parts);

                // Détail (max 3)
                var lines = new List<string> { summary };
                int max = 3;
                for (int i = 0; i < System.Math.Min(realResolved.Count, max); i++)
                    lines.Add($"• {realResolved[i].Message}");
                if (realResolved.Count > max)
                    lines.Add($"… et {realResolved.Count - max} autre(s)");

                return string.Join("\n", lines);
            }

            // ---- Nouveaux problèmes (sans changement de statut global) ----
            if (alert.NewProblems != null && alert.NewProblems.Count > 0 && alert.Status != "ok")
            {
                var lines = new List<string>();
                int maxShown = 3;
                for (int i = 0; i < System.Math.Min(alert.NewProblems.Count, maxShown); i++)
                {
                    var p = alert.NewProblems[i];
                    string prefix = (p.Severity == "critical" || p.Severity == "high") ? "[CRIT]" : "[WARN]";
                    lines.Add($"{prefix} {p.Message}");
                }
                if (alert.NewProblems.Count > maxShown)
                    lines.Add($"... et {alert.NewProblems.Count - maxShown} autre(s)");
                return string.Join("\n", lines);
            }

            // ---- Offline ----
            if (alert.Status == "offline")
                return "Le serveur ne répond plus";

            // ---- Problèmes en cours (cas standard) ----
            if (alert.Problems != null && alert.Problems.Count > 0)
            {
                int toCheckCount = alert.Problems.Count(p =>
                    string.Equals(p.Severity, "tocheck", StringComparison.OrdinalIgnoreCase));

                var lines = new List<string>();

                // Préambule ToCheck (guidance utilisateur)
                if (toCheckCount > 0)
                {
                    lines.Add($"⚠ {toCheckCount} alerte(s) à vérifier — cliquer « Safe ? » dans l'interface");
                }

                int maxShown = toCheckCount > 0 ? 2 : 3;
                for (int i = 0; i < System.Math.Min(alert.Problems.Count, maxShown); i++)
                {
                    var p = alert.Problems[i];
                    string prefix = p.Severity switch
                    {
                        "critical" => "[CRIT]",
                        "high"     => "[CRIT]",
                        "tocheck"  => "[À VERIF]",
                        _          => "[WARN]"
                    };
                    lines.Add($"{prefix} {p.Message}");
                }
                if (alert.Problems.Count > maxShown)
                    lines.Add($"... et {alert.Problems.Count - maxShown} autre(s)");
                return string.Join("\n", lines);
            }

            return alert.FirstProblem ?? "";
        }

        // ---------------------------------------------
        //  Large icon : chargement + mapping
        // ---------------------------------------------
        private static global::Android.Graphics.Bitmap? LoadLargeIcon(Context context, string iconName)
        {
            try
            {
                int resId = context.Resources!.GetIdentifier(
                    iconName, "drawable", context.PackageName);

                if (resId == 0) return null;

                return global::Android.Graphics.BitmapFactory.DecodeResource(context.Resources, resId);
            }
            catch
            {
                return null;
            }
        }

        private static string GetAlertIconName(PushAlert alert)
        {
            // ---- NetMonitor : menace neutralisée (inchangé) ----
            if (alert.Status == "ok" && alert.LastBlock != null &&
                (DateTime.UtcNow - alert.LastBlock.Timestamp.ToUniversalTime()).TotalMinutes < 5)
            {
                return "ok";
            }

            // ---- Résolution : problème(s) résolu(s) ----
            if (alert.Status == "ok" && alert.PreviousStatus != "ok" &&
                alert.ResolvedProblems != null && alert.ResolvedProblems.Count > 0)
            {
                int disk = 0, cpu = 0, ram = 0, gpu = 0;
                foreach (var p in alert.ResolvedProblems)
                {
                    var k = Classify(p);
                    if      (IsDiskKind(k))              disk++;
                    else if (IsCpuKind(k))               cpu++;
                    else if (k == ProblemKind.RamUsage)  ram++;
                    else if (k == ProblemKind.GpuTemp)   gpu++;
                }

                int groups = 0;
                if (disk > 0) groups++;
                if (cpu  > 0) groups++;
                if (ram  > 0) groups++;
                if (gpu  > 0) groups++;

                // Cas groupé : plusieurs catégories distinctes ? icône générique
                if (groups > 1) return "systemok";

                // Cas simple : une seule catégorie
                if (disk > 0) return "romok";
                if (cpu  > 0) return "cpuok";
                if (ram  > 0) return "ramok";
                if (gpu  > 0) return "cpuok";

                return "check";
            }

            // ---- Problèmes en cours ----
            if (alert.Problems != null && alert.Problems.Count > 0)
            {
                // PRIORITÉ 1 : ToCheck (violet) — décision manuelle requise
                if (alert.Problems.Any(p =>
                    string.Equals(p.Severity, "tocheck", StringComparison.OrdinalIgnoreCase)))
                {
                    return "tocheck";
                }
                int disk = 0, cpuU = 0, cpuT = 0, ram = 0, gpu = 0;
                foreach (var p in alert.Problems)
                {
                    var k = Classify(p);
                    if (k == ProblemKind.DiskSpace || k == ProblemKind.DiskSmart || k == ProblemKind.DiskTemp) disk++;
                    else if (k == ProblemKind.CpuTemp)   cpuT++;
                    else if (k == ProblemKind.CpuUsage)  cpuU++;
                    else if (k == ProblemKind.RamUsage)  ram++;
                    else if (k == ProblemKind.GpuTemp)   gpu++;
                }

                int groups = 0;
                if (disk > 0) groups++;
                if (cpuU > 0 || cpuT > 0) groups++;
                if (ram  > 0) groups++;
                if (gpu  > 0) groups++;

                // Cas groupé ? icône générique (underscore pour Android)
                if (groups > 1) return "system_critical";

                // Cas simple ? icône spécifique
                if (disk > 0) return "hdd_critical";
                if (cpuT > 0) return "cpu_temp";
                if (cpuU > 0) return "cpu_overload";
                if (ram  > 0) return "ram_overload";
                if (gpu  > 0) return "cpu_overload";
            }

            return alert.Status switch
            {
                "critical" => "critical",
                "warning"  => "warning",
                "offline"  => "disconnected",
                _          => "connected"
            };
        }

        private static string GetMediaIconName(string? mediaType, string eventType)
        {
            bool isStop = eventType == "stopped";
            string t = (mediaType ?? "").Trim().ToLowerInvariant();

            // --- Cas particulier : enregistrement DVBViewer ---
            // Noms de ressources Android : underscore obligatoire (pas de tiret)
            if (IsRecordingType(t))
                return isStop ? "dvbviewer_stop" : "dvbviewer_rec";

            string prefix   = isStop ? "stop_" : "play_";
            string fallback = isStop ? "stop" : "play";

            string suffix = t switch
            {
                "audio" or "music" or "musique"          => "audio",
                "video" or "movie" or "film"             => "video",
                "serie" or "series" or "tv" or "episode" => "tv",
                "image" or "photo" or "picture"          => "image",
                _                                        => ""
            };

            return string.IsNullOrEmpty(suffix) ? fallback : prefix + suffix;
        }

        private static bool IsAudioType(string? mediaType)
        {
            string t = (mediaType ?? "").Trim().ToLowerInvariant();
            return t == "audio" || t == "music" || t == "musique";
        }

        private static bool IsRecordingType(string? mediaType)
        {
            string t = (mediaType ?? "").Trim().ToLowerInvariant();
            return t == "rec" || t == "recording" || t == "enregistrement" || t == "dvbviewer";
        }

        private static bool IsDiskProblem(ProblemItem p)
        {
            string cat = (p.Category ?? "").ToLowerInvariant();
            string msg = (p.Message ?? "").ToLowerInvariant();

            return cat.Contains("disk")
                || cat.Contains("smart")
                || cat.Contains("hdd")
                || cat.Contains("ssd")
                || cat.Contains("storage")
                || msg.Contains("disk")
                || msg.Contains("disque")
                || msg.Contains("smart")
                || msg.Contains("hdd");
        }

        private static bool IsCpuTempProblem(ProblemItem p)
        {
            string cat = (p.Category ?? "").ToLowerInvariant();
            string msg = (p.Message ?? "").ToLowerInvariant();

            return (cat.Contains("cpu") && (cat.Contains("temp") || cat.Contains("thermal")))
                || (msg.Contains("cpu") && (msg.Contains("temp") || msg.Contains("chaud") || msg.Contains("°c")))
                || cat.Contains("temperature")
                || cat.Contains("thermal");
        }

        private static bool IsCpuProblem(ProblemItem p)
        {
            string cat = (p.Category ?? "").ToLowerInvariant();
            string msg = (p.Message ?? "").ToLowerInvariant();

            return cat.Contains("cpu")
                || cat.Contains("processor")
                || msg.Contains("cpu")
                || msg.Contains("processeur");
        }
        
        private static bool IsRamProblem(ProblemItem p)
        {
            string cat = (p.Category ?? "").ToLowerInvariant();
            string msg = (p.Message ?? "").ToLowerInvariant();

            return cat.Contains("ram") 
                || cat.Contains("memory") 
                || cat.Contains("memoire")
                || msg.Contains("ram") 
                || msg.Contains("memory") 
                || msg.Contains("memoire");
        }
        // ============================================================
        //  Classification des problèmes (équivalent de Windows)
        // ============================================================
        private enum ProblemKind
        {
            Unknown, CpuUsage, CpuTemp, RamUsage,
            DiskSpace, DiskSmart, DiskTemp, GpuTemp, Bsod
        }

        private static ProblemKind Classify(ProblemItem p)
        {
            switch ((p.Category ?? "").Trim().ToLowerInvariant())
            {
                case "smart":       return ProblemKind.DiskSmart;
                case "diskspace":   return ProblemKind.DiskSpace;
                case "cpuhigh":     return ProblemKind.CpuUsage;
                case "ramhigh":     return ProblemKind.RamUsage;
                case "temphigh":    return ProblemKind.CpuTemp;
                case "gputemphigh": return ProblemKind.GpuTemp;
                case "bsod":        return ProblemKind.Bsod;
            }

            // Fallback pour futurs moniteurs
            string msg = (p.Message ?? "").ToLowerInvariant();
            if (msg.Contains("cpu") && (msg.Contains("temp") || msg.Contains("°c") || msg.Contains("therm"))) return ProblemKind.CpuTemp;
            if (msg.Contains("cpu") || msg.Contains("processeur")) return ProblemKind.CpuUsage;
            if (msg.Contains("ram") || msg.Contains("mémoire") || msg.Contains("memory")) return ProblemKind.RamUsage;
            if (msg.Contains("gpu") || msg.Contains("nvidia") || msg.Contains("geforce")) return ProblemKind.GpuTemp;
            if (msg.Contains("smart")) return ProblemKind.DiskSmart;
            if (msg.Contains("espace") || msg.Contains("disque") || msg.Contains("disk")) return ProblemKind.DiskSpace;
            return ProblemKind.Unknown;
        }

        private static bool IsDiskKind(ProblemKind k) =>
            k == ProblemKind.DiskSpace || k == ProblemKind.DiskSmart || k == ProblemKind.DiskTemp;

        private static bool IsCpuKind(ProblemKind k) =>
            k == ProblemKind.CpuUsage || k == ProblemKind.CpuTemp;

        private static string KindLabel(ProblemKind k) => k switch
        {
            ProblemKind.DiskSpace => "Disque (espace)",
            ProblemKind.DiskSmart => "Disque (SMART)",
            ProblemKind.DiskTemp  => "Disque (température)",
            ProblemKind.CpuUsage  => "CPU (surcharge)",
            ProblemKind.CpuTemp   => "CPU (température)",
            ProblemKind.RamUsage  => "RAM",
            ProblemKind.GpuTemp   => "GPU",
            _                     => ""
        };

        private static string KindIcon(ProblemKind k) => k switch
        {
            // ?? Noms de ressources Android : underscore obligatoire
            ProblemKind.DiskSpace => "romok",
            ProblemKind.DiskSmart => "romok",
            ProblemKind.DiskTemp  => "romok",
            ProblemKind.CpuUsage  => "cpuok",
            ProblemKind.CpuTemp   => "cpuok",
            ProblemKind.RamUsage  => "ramok",
            ProblemKind.GpuTemp   => "cpuok",
            _                     => "check"
        };                
    }
}