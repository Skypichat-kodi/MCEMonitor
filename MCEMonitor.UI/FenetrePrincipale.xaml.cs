using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MailKit.Net.Smtp;
using MailKit.Security;
using MCEMonitor.Languages;
using MCEMonitor.Utils;
using MimeKit;

namespace MCEMonitor.UI
{
    public partial class FenetrePrincipale : Window
    {
        public FenetrePrincipale()
        {
            InitializeComponent();

            // Journal : tous les clics sur des boutons et tous les changements
            // d'onglet ou de liste sont tracés, sans instrumenter chaque handler.
            AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(JournaliserClic), true);
            AddHandler(Selector.SelectionChangedEvent, new RoutedEventHandler(JournaliserSelection), true);

            ChargerConfigEmail();
            ChargerAbout();
            ChargerMedia();
            ChargerRom();
            ChargerSystem();
            ChargerWake();
            ChargerStop();
            ChargerOnOff();
            ChargerNet();

            // Les modules restent masqués tant que l'envoi d'emails n'est pas
            // opérationnel, comme dans la version WinForms.
            VerrouillerOngletsModules();

            // Bandeau d'avertissement si la machine tourne sur batterie.
            ChargerAlimentation();

            // Les tâches planifiées peuvent être créées ou supprimées ailleurs
            // (autre session, planificateur Windows) : on relit leur état.
            ChargerMinuteurTaches();

            // Photo des taches planifiees a l ouverture, ecrite dans le journal.
            _ = RafraichirToutesLesTachesAsync(journaliser: true);

            // Les interfaces des modules réécrivent leurs fichiers de configuration :
            // on recharge les champs quand cela arrive.
            ChargerSurveillanceConfigs();

            // L'état du serveur est vérifié une fois la fenêtre affichée, pour ne
            // pas retarder son ouverture par un contrôle réseau.
            Loaded += async (s, e) =>
            {
                // La zone de licence doit s'ouvrir sur son début, pas sur sa fin.
                CadreLicence.ScrollToTop();

                await VerifierSmtpAuDemarrageAsync();
            };
        }

        // ============================================================
        //  Déplacement de la fenêtre par l'en-tête, la barre de titre
        //  du système étant désactivée (WindowStyle=None)
        // ============================================================
        // ============================================================
        //  Journalisation des événements de l'interface
        // ============================================================
        private void JournaliserClic(object sender, RoutedEventArgs e)
        {
            DependencyObject? source = e.OriginalSource as DependencyObject;

            while (source is not null and not ButtonBase)
                source = ParentDe(source);

            if (source is not ButtonBase bouton)
                return;

            CoreLog.Info("UI", "Clic : " + NomDuBouton(bouton));
        }

        private void JournaliserSelection(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is TabControl onglets)
            {
                // Le Header est le plus souvent une chaine traduite (loc:Tr) ; il peut
                // aussi etre un panneau (icone + libelle), d ou les deux lectures.
                string titre = "?";

                if (onglets.SelectedItem is TabItem item)
                {
                    titre = item.Header as string
                            ?? TexteDe(item.Header as DependencyObject)
                            ?? "?";
                }

                CoreLog.Info("UI", "Onglet : " + titre);

                // Le journal du test SMTP ne concerne que l'onglet Email : il disparaît
                // dès qu'on le quitte, et revient au prochain test.
                if (onglets.SelectedItem is TabItem selectionne && selectionne != OngletEmail)
                    CadreJournal.Visibility = Visibility.Collapsed;
            }
            else if (e.OriginalSource is ComboBox liste)
            {
                string valeur = (liste.SelectedItem as ComboBoxItem)?.Content?.ToString()
                                 ?? liste.Text ?? string.Empty;

                // Les selections vides correspondent a l initialisation des listes :
                // elles n apportent rien au journal.
                if (string.IsNullOrWhiteSpace(valeur))
                    return;

                string nom = string.IsNullOrWhiteSpace(liste.Name) ? "(liste)" : liste.Name;
                CoreLog.Info("UI", $"Liste {nom} : {valeur}");
            }
        }

        private static DependencyObject? ParentDe(DependencyObject objet)
            => objet is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(objet)
                : LogicalTreeHelper.GetParent(objet);

        private static string NomDuBouton(ButtonBase bouton)
        {
            // 1. le nom XAML, quand il existe : c'est le plus precis
            if (!string.IsNullOrWhiteSpace(bouton.Name))
                return bouton.Name;

            // 2. l'infobulle : par exemple « Fermer » pour le bouton ✕
            if (bouton.ToolTip is string infobulle && !string.IsNullOrWhiteSpace(infobulle))
                return infobulle.Trim();

            // 3. le libelle porte directement par le bouton
            if (bouton.Content is string texte && !string.IsNullOrWhiteSpace(texte))
                return texte.Trim();

            // 4. le libelle dessine dans son contenu (icone + texte)
            string? trouve = TexteDe(bouton.Content as DependencyObject);
            return string.IsNullOrWhiteSpace(trouve) ? "(bouton)" : trouve;
        }

        private static string? TexteDe(DependencyObject? racine)
        {
            if (racine is null)
                return null;

            if (racine is TextBlock bloc && !string.IsNullOrWhiteSpace(bloc.Text))
                return bloc.Text;

            if (racine is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                int enfants = VisualTreeHelper.GetChildrenCount(racine);

                for (int i = 0; i < enfants; i++)
                {
                    string? texte = TexteDe(VisualTreeHelper.GetChild(racine, i));

                    if (!string.IsNullOrWhiteSpace(texte))
                        return texte;
                }
            }

            // En-tetes et contenus pas encore realises visuellement : arbre logique.
            foreach (object enfant in LogicalTreeHelper.GetChildren(racine))
            {
                if (enfant is DependencyObject objet)
                {
                    string? texte = TexteDe(objet);

                    if (!string.IsNullOrWhiteSpace(texte))
                        return texte;
                }
            }

            return null;
        }

        private void EnTete_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // Le bouton a été relâché avant le début du glisser
                }
            }
        }

        private void SurReduire(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void SurFermer(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ============================================================
        //  Rafraîchissement périodique de l'état des tâches planifiées
        // ============================================================

        private readonly DispatcherTimer _minuteurTaches = new() { Interval = TimeSpan.FromSeconds(30) };

        private void ChargerMinuteurTaches()
        {
            _minuteurTaches.Tick += async (s, e) => await RafraichirToutesLesTachesAsync();
            _minuteurTaches.Start();
        }

        private async Task RafraichirToutesLesTachesAsync(bool journaliser = false)
        {
            try
            {
                // Les interrogations passent par schtasks : hors du thread d'interface,
                // sinon la fenêtre se figerait à chaque cycle.
                var etats = await Task.Run(() => new[]
                {
                    TaskSchedulerHelper.MediaMonitorServiceTaskExists(),
                    TaskSchedulerHelper.RomMonitorTaskExists(),
                    TaskSchedulerHelper.SystemMonitorTaskExists(),
                    TaskSchedulerHelper.NetMonitorTaskExists(),
                    TaskSchedulerHelper.WakeTaskExists(),
                    TaskSchedulerHelper.StopTaskExists(),
                    TaskSchedulerHelper.ShutdownTaskExists()
                });

                _tacheMediaExiste  = etats[0];
                _tacheRomExiste    = etats[1];
                _tacheSystemExiste = etats[2];
                _tacheNetExiste    = etats[3];
                _tacheWakeExiste   = etats[4];
                _tacheStopExiste   = etats[5];
                _tacheArretExiste  = etats[6];

                MetAJourBoutonAutomatique();
                MetAJourBoutonAutomatiqueRom();
                MetAJourBoutonAutomatiqueSystem();
                MetAJourBoutonAutomatiqueNet();
                MetAJourBoutonAutomatiqueWake();
                MetAJourBoutonAutomatiqueStop();
                MetAJourBoutonAutomatiqueArret();

                // Au demarrage seulement : le minuteur de 30 s ne doit pas noyer le
                // journal, mais on veut savoir ce qui etait actif a l ouverture.
                if (journaliser)
                    JournaliserEtatTaches();
            }
            catch
            {
                // État inconnu : les boutons conservent leur libellé précédent.
            }
        }

        // ============================================================
        //  Surveillance des fichiers de configuration des modules
        // ============================================================

        private readonly DispatcherTimer _minuteurConfigs = new() { Interval = TimeSpan.FromSeconds(3) };

        private (DateTime ecriture, long taille) _etatConfigRom;
        private (DateTime ecriture, long taille) _etatConfigSystem;
        private (DateTime ecriture, long taille) _etatConfigNet;

        private static (DateTime, long) Empreinte(string chemin)
        {
            try
            {
                if (!File.Exists(chemin))
                    return (DateTime.MinValue, 0);

                var info = new FileInfo(chemin);
                return (info.LastWriteTimeUtc, info.Length);
            }
            catch
            {
                return (DateTime.MinValue, 0);
            }
        }

        private void ChargerSurveillanceConfigs()
        {
            _etatConfigRom    = Empreinte(FichierConfigRom);
            _etatConfigSystem = Empreinte(FichierConfigSystem);
            _etatConfigNet    = Empreinte(FichierConfigNet);

            _minuteurConfigs.Tick += (s, e) => VerifierConfigsModifiees();
            _minuteurConfigs.Start();
        }

        private void VerifierConfigsModifiees()
        {
            var rom = Empreinte(FichierConfigRom);
            if (rom != _etatConfigRom) { _etatConfigRom = rom; ChargerReglagesRom(); }

            var system = Empreinte(FichierConfigSystem);
            if (system != _etatConfigSystem) { _etatConfigSystem = system; ChargerReglagesSystem(); }

            var net = Empreinte(FichierConfigNet);
            if (net != _etatConfigNet) { _etatConfigNet = net; ChargerReglagesNet(); }
        }

        // ============================================================
        //  Alimentation : bandeau d'avertissement sur batterie
        // ============================================================

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS statut);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;      // 0 = sur batterie, 1 = sur secteur, 255 = inconnu
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int  BatteryLifeTime;
            public int  BatteryFullLifeTime;
        }

        private void ChargerAlimentation()
        {
            MetAJourBandeauBatterie();

            // WM_POWERBROADCAST arrive immédiatement au changement de source
            // d'alimentation : bien plus réactif qu'un contrôle périodique.
            SourceInitialized += (s, e) =>
            {
                if (PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource source)
                    source.AddHook(MessageAlimentation);
            };
        }

        private const int WM_POWERBROADCAST        = 0x0218;
        private const int PBT_APMPOWERSTATUSCHANGE = 0x000A;

        private IntPtr MessageAlimentation(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool traite)
        {
            if (message == WM_POWERBROADCAST && wParam.ToInt32() == PBT_APMPOWERSTATUSCHANGE)
                MetAJourBandeauBatterie();

            return IntPtr.Zero;
        }

        private void MetAJourBandeauBatterie()
        {
            bool surBatterie = false;

            try
            {
                if (GetSystemPowerStatus(out var statut))
                    surBatterie = statut.ACLineStatus == 0;
            }
            catch
            {
                // Pas d'information d'alimentation : on n'affiche rien.
            }

            BandeauBatterie.Visibility = surBatterie ? Visibility.Visible : Visibility.Collapsed;
        }

        // ============================================================
        //  ONGLET EMAIL
        // ============================================================

        private void ChargerConfigEmail()
        {
            var cfg = EmailConfig.Load();

            ChampSmtpServer.Text       = cfg.Server;
            ChampSmtpPort.Text         = cfg.Port.ToString();
            ChampExpediteur.Text       = cfg.From;
            ChampMotDePasse.Password   = cfg.Password;
            ChampDestinataire.Text     = cfg.To;

            foreach (var item in ChoixSecurite.Items)
            {
                if (item is System.Windows.Controls.ComboBoxItem choix &&
                    string.Equals(choix.Content?.ToString(), cfg.SecurityMode,
                                  StringComparison.OrdinalIgnoreCase))
                {
                    ChoixSecurite.SelectedItem = choix;
                    break;
                }
            }

            if (ChoixSecurite.SelectedItem == null && ChoixSecurite.Items.Count > 0)
                ChoixSecurite.SelectedIndex = 0;
        }

        // Le mot de passe est saisi soit masqué, soit en clair : les deux contrôles
        // coexistent et l'on ne lit que celui qui est visible.
        private string MotDePasseSaisi =>
            ChampMotDePasseVisible.Visibility == Visibility.Visible
                ? ChampMotDePasseVisible.Text
                : ChampMotDePasse.Password;

        private void SurAfficherMotDePasse(object sender, RoutedEventArgs e)
        {
            if (ChampMotDePasse.Visibility == Visibility.Visible)
            {
                ChampMotDePasseVisible.Text = ChampMotDePasse.Password;

                ChampMotDePasse.Visibility        = Visibility.Collapsed;
                ChampMotDePasseVisible.Visibility = Visibility.Visible;

                BoutonAfficher.Content = LanguageManager.Get("Masquer") ?? "Masquer";
            }
            else
            {
                ChampMotDePasse.Password = ChampMotDePasseVisible.Text;

                ChampMotDePasseVisible.Visibility = Visibility.Collapsed;
                ChampMotDePasse.Visibility        = Visibility.Visible;

                BoutonAfficher.Content = LanguageManager.Get("Afficher") ?? "Afficher";
            }
        }

        private async void SurEnregistrerEmail(object sender, RoutedEventArgs e)
        {
            var cfg = new EmailConfig
            {
                Server       = ChampSmtpServer.Text.Trim(),
                Port         = int.TryParse(ChampSmtpPort.Text, out int port) ? port : 465,
                From         = ChampExpediteur.Text.Trim(),
                Password     = MotDePasseSaisi.Trim(),
                To           = ChampDestinataire.Text.Trim(),
                SecurityMode = ModeSecuriteChoisi()
            };

            cfg.Save();

            CoreLog.Info("Mail", $"Configuration enregistrée : {cfg.Server}:{cfg.Port}, "
                               + $"{cfg.SecurityMode}, expéditeur {cfg.From}");

            Confirmer(LanguageManager.Get("Configuration Email enregistrée")
                      ?? "Configuration Email enregistrée");

            // Le contrôle repart du fichier qui vient d'être écrit : cache vidé (0).
            // Les modules suivent le résultat : si le serveur ne répond plus, ils
            // se masquent ; s'il répond, ils reviennent.
            BoutonEnregistrer.IsEnabled = false;

            try
            {
                var statut = await SmtpHealthChecker.CheckAsync(0);
                AppliquerStatutSmtp(statut);
                AppliquerVerrouillageSelonStatut(statut);
            }
            finally
            {
                BoutonEnregistrer.IsEnabled = true;
            }
        }

        private string ModeSecuriteChoisi()
        {
            if (ChoixSecurite.SelectedItem is System.Windows.Controls.ComboBoxItem choix)
                return choix.Content?.ToString() ?? "SSL";

            return "SSL";
        }

        private async void SurTesterEmail(object sender, RoutedEventArgs e)
        {
            // Comme dans la version WinForms, le test porte sur la configuration
            // enregistrée : les modifications non enregistrées ne sont pas prises
            // en compte.
            var cfg = EmailConfig.Load();

            CadreJournal.Visibility = Visibility.Visible;
            ZoneJournal.Clear();
            BoutonTester.IsEnabled = false;
            TexteConfirmation.Visibility = Visibility.Collapsed;

            CoreLog.Info("Mail", "Test SMTP demandé");

            Journaliser(LanguageManager.Get("Démarrage du test SMTP…") ?? "Démarrage du test SMTP…");

            try
            {
                Journaliser(
                    (LanguageManager.Get("Résolution DNS du serveur") ?? "Résolution DNS du serveur") +
                    $" {cfg.Server}…");

                var addresses = await Dns.GetHostAddressesAsync(cfg.Server);
                if (addresses.Length > 0)
                    Journaliser($"IP : {addresses[0]}");

                Journaliser(
                    (LanguageManager.Get("Connexion au serveur SMTP sur le port")
                     ?? "Connexion au serveur SMTP sur le port") +
                    $" {cfg.Port}…");

                var options = cfg.SecurityMode.ToUpperInvariant() switch
                {
                    "SSL"      => SecureSocketOptions.SslOnConnect,
                    "TLS"      => SecureSocketOptions.StartTls,
                    "STARTTLS" => SecureSocketOptions.StartTls,
                    "NONE"     => SecureSocketOptions.None,
                    _          => SecureSocketOptions.Auto
                };

                using var client = new SmtpClient();

                await client.ConnectAsync(cfg.Server, cfg.Port, options);
                Journaliser($"Connexion établie ({options})");

                Journaliser(LanguageManager.Get("Authentification…") ?? "Authentification…");
                await client.AuthenticateAsync(cfg.From, cfg.Password);
                Journaliser(LanguageManager.Get("Authentification réussie.")
                            ?? "Authentification réussie.");

                Journaliser(LanguageManager.Get("Envoi du message de test…")
                            ?? "Envoi du message de test…");

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("MCEMonitor", cfg.From));
                message.To.Add(new MailboxAddress(cfg.To, cfg.To));
                message.Subject = "Test SMTP MCEMonitor";
                message.Body    = new TextPart("plain") { Text = "Ceci est un test SMTP." };

                await client.SendAsync(message);
                Journaliser(LanguageManager.Get("Email envoyé avec succès !")
                            ?? "Email envoyé avec succès !");

                await client.DisconnectAsync(true);
                Journaliser(LanguageManager.Get("Déconnexion du serveur.")
                            ?? "Déconnexion du serveur.");

                SmtpHealthChecker.MarkAsReady();
                AppliquerStatutSmtp(SmtpStatus.Ready);
                DeverrouillerOngletsModules();

                Confirmer(LanguageManager.Get("Email envoyé avec succès !")
                          ?? "Email envoyé avec succès !");

                CoreLog.Info("Mail", "Test SMTP réussi");
            }
            catch (Exception ex)
            {
                CoreLog.Warn("Mail", "Test SMTP échoué : " + ex.Message);

                Journaliser((LanguageManager.Get("ERREUR : ") ?? "ERREUR : ") + ex.Message);
                await RafraichirStatutSmtpAsync(0);
            }
            finally
            {
                BoutonTester.IsEnabled = true;
            }
        }

        private void Journaliser(string ligne)
        {
            ZoneJournal.AppendText($"[{DateTime.Now:HH:mm:ss}] {ligne}{Environment.NewLine}");
            ZoneJournal.ScrollToEnd();
        }

        private void Confirmer(string message)
        {
            TexteConfirmation.Text       = "✔ " + message;
            TexteConfirmation.Visibility = Visibility.Visible;
        }

        // ============================================================
        //  État du serveur SMTP
        // ============================================================

        // ============================================================
        //  Verrouillage des modules tant que l'email n'est pas opérationnel
        // ============================================================

        private System.Windows.Controls.TabItem[] OngletsModules => new[]
        {
            OngletMedia, OngletRom, OngletSystem, OngletNet, OngletWake, OngletStop
        };

        private void VerrouillerOngletsModules()
        {
            foreach (var onglet in OngletsModules)
                onglet.Visibility = Visibility.Collapsed;

            CoreLog.Info("Mail", "Onglets modules verrouillés (SMTP indisponible)");
        }

        private void DeverrouillerOngletsModules()
        {
            foreach (var onglet in OngletsModules)
                onglet.Visibility = Visibility.Visible;

            CoreLog.Info("Mail", "Onglets modules débloqués (SMTP opérationnel)");
        }

        // Un module masqué ne peut pas rester sélectionné : on retombe sur l'onglet Email.
        private void AppliquerVerrouillageSelonStatut(SmtpStatus statut)
        {
            if (statut == SmtpStatus.Ready)
            {
                DeverrouillerOngletsModules();
                return;
            }

            if (OngletsModules.Any(o => o.IsSelected))
                OngletEmail.IsSelected = true;

            VerrouillerOngletsModules();
        }

        private async Task VerifierSmtpAuDemarrageAsync()
        {
            // Contrôle sans cache : au démarrage, on veut l'état réel.
            var statut = await SmtpHealthChecker.CheckAsync(0);
            AppliquerStatutSmtp(statut);

            CoreLog.Info("Mail", $"Contrôle SMTP au démarrage : {statut}");

            if (statut == SmtpStatus.Ready)
            {
                DeverrouillerOngletsModules();
                return;
            }

            Boite.Show(this,
                LanguageManager.Get("Ui.SmtpRequired") ?? "L'envoi d'Email est impossible. Vous devez obligatoirement entrer un SMTP valide pour pouvoir profiter de l'ensemble de MCEMonitor.",
                LanguageManager.Get("Configuration Email requise") ?? "Configuration Email requise",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            OngletEmail.IsSelected = true;
        }

        private async Task RafraichirStatutSmtpAsync(int cacheMinutes = 10)
        {
            var status = await SmtpHealthChecker.CheckAsync(cacheMinutes);
            AppliquerStatutSmtp(status);

            CoreLog.Info("Mail", $"Contrôle SMTP : {status}");
        }

        private void AppliquerStatutSmtp(SmtpStatus status)
        {
            Color couleur = status switch
            {
                SmtpStatus.Ready                            => Color.FromRgb(76, 175, 80),
                SmtpStatus.NotConfigured                    => Color.FromRgb(160, 160, 160),
                SmtpStatus.Unreachable or SmtpStatus.AuthFailed => Color.FromRgb(220, 60, 60),
                _                                           => Color.FromRgb(230, 150, 30)
            };

            PastilleStatut.Fill = new SolidColorBrush(couleur);

            TexteStatut.Text = status switch
            {
                SmtpStatus.Ready         => LanguageManager.Get("Connexion SMTP opérationnelle.")
                                            ?? "Connexion SMTP opérationnelle.",
                SmtpStatus.NotConfigured => LanguageManager.Get("Configuration email incomplète.")
                                            ?? "Configuration email incomplète.",
                SmtpStatus.Unreachable   => LanguageManager.Get("Le serveur SMTP ne répond pas.")
                                            ?? "Le serveur SMTP ne répond pas.",
                SmtpStatus.AuthFailed    => LanguageManager.Get("Identifiants SMTP refusés.")
                                            ?? "Identifiants SMTP refusés.",
                _                        => LanguageManager.Get("Statut SMTP inconnu.")
                                            ?? "Statut SMTP inconnu."
            };
        }

        // ============================================================
        //  ONGLET MEDIAMONITOR
        // ============================================================

        private readonly DispatcherTimer _minuteurService  = new() { Interval = TimeSpan.FromSeconds(3) };
        private readonly DispatcherTimer _minuteurRapports = new() { Interval = TimeSpan.FromSeconds(10) };

        // Empêche le minuteur de rabattre l'interrupteur pendant une action.
        private bool _basculeEnCours;
        private bool _tacheMediaExiste;

        private static string CheminServiceMedia => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "MediaMonitor.Service.exe");

        private static bool ServiceMediaEnCours =>
            Process.GetProcessesByName("MediaMonitor.Service").Length > 0;

        private static bool InterfaceMediaOuverte =>
            Process.GetProcessesByName("MediaMonitor.UI").Length > 0;

        private void ChargerMedia()
        {
            MetAJourEtatService();
            MetAJourLibellesRapports();
            MetAJourBoutonAutomatique();

            // Sans élévation, Windows refuse de lire l'état d'une tâche planifiée
            // créée en SYSTEM : le bouton resterait rouge sans explication.
            if (!EstEleve())
            {
                AfficherMessage(
                    "Fenêtre non élevée : Windows refusera la création et la lecture des tâches planifiées. " +
                    "Ce message ne doit pas apparaître dans la version livrée, qui exige l'élévation.",
                    alerte: true);
            }

            _minuteurService.Tick  += (s, e) => MetAJourEtatService();
            _minuteurRapports.Tick += (s, e) => MetAJourLibellesRapports();
            _minuteurService.Start();
            _minuteurRapports.Start();

            // La détection de la tâche planifiée passe par schtasks : hors du thread
            // d'interface, pour ne pas figer la fenêtre à l'ouverture.
            _ = RafraichirTacheMediaAsync();
        }

        private static bool EstEleve()
        {
            try
            {
                using var identite = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identite).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private async Task RafraichirTacheMediaAsync()
        {
            try
            {
                _tacheMediaExiste = await Task.Run(TaskSchedulerHelper.MediaMonitorServiceTaskExists);
                MetAJourBoutonAutomatique();
            }
            catch
            {
                // État inconnu : le bouton conserve son libellé précédent.
            }
        }

        /// <summary>
        /// Écrit dans le journal l'état des sept tâches planifiées tel qu'il vient
        /// d'être relevé : on sait ainsi ce qui était actif à l'ouverture.
        /// </summary>
        private void JournaliserEtatTaches()
        {
            var releve = new (string Tache, bool Active)[]
            {
                ("MCEMonitor_MediaMonitorService",  _tacheMediaExiste),
                ("MCEMonitor_RomMonitorService",    _tacheRomExiste),
                ("MCEMonitor_SystemMonitorService", _tacheSystemExiste),
                ("MCEMonitor_NetMonitorService",    _tacheNetExiste),
                ("MCEMonitor_WakeMonitor",          _tacheWakeExiste),
                ("MCEMonitor_StopMonitor",          _tacheStopExiste),
                ("MCEMonitor_Shutdown",             _tacheArretExiste)
            };

            int actives = 0;

            foreach ((string tache, bool active) in releve)
            {
                if (active)
                    actives++;

                CoreLog.Info("Tâches", $"{tache} : {(active ? "active" : "inactive")}");
            }

            CoreLog.Info("Tâches", $"État au démarrage : {actives}/{releve.Length} active(s)");
        }

        private void MetAJourBoutonAutomatique()
        {
            BoutonAutomatique.Content = _tacheMediaExiste
                ? (LanguageManager.Get("Automatique ON")  ?? "Automatique ON")
                : (LanguageManager.Get("Automatique OFF") ?? "Automatique OFF");

            // Vert quand la tâche existe, rouge sinon : même code couleur que WinForms.
            BoutonAutomatique.Style = (Style)FindResource(
                _tacheMediaExiste ? "BoutonSucces" : "BoutonDanger");
        }

        private void MetAJourEtatService()
        {
            if (_basculeEnCours)
                return;

            bool actif = ServiceMediaEnCours;

            BasculeService.IsChecked = actif;

            TexteEtatService.Text = actif
                ? (LanguageManager.Get("Service MediaMonitor : actif")  ?? "Service MediaMonitor : actif")
                : (LanguageManager.Get("Service MediaMonitor : arrêté") ?? "Service MediaMonitor : arrêté");
        }

        private async void SurBasculeService(object sender, RoutedEventArgs e)
        {
            if (_basculeEnCours)
                return;

            _basculeEnCours = true;

            CoreLog.Info("MediaMonitor", "Service : "
                                + (ServiceMediaEnCours ? "arrêt demandé" : "démarrage demandé"));

            try
            {
                if (ServiceMediaEnCours)
                {
                    if (InterfaceMediaOuverte)
                    {
                        BasculeService.IsChecked = true;
                        AfficherMessage(
                            LanguageManager.Get("Impossible d'arrêter MediaMonitor.Service tant que MediaMonitor.UI est ouvert. Veuillez fermer MediaMonitor.UI d'abord.")
                            ?? "Impossible d'arrêter MediaMonitor.Service tant que MediaMonitor.UI est ouvert. Veuillez fermer MediaMonitor.UI d'abord.",
                            alerte: true);
                        return;
                    }

                    foreach (var processus in Process.GetProcessesByName("MediaMonitor.Service"))
                        processus.Kill();
                }
                else
                {
                    if (!await DemarrerServiceMediaAsync())
                        return;

                    DemarrerTrayMedia();
                }
            }
            catch (Exception ex)
            {
                AfficherMessage((LanguageManager.Get("Erreur : ") ?? "Erreur : ") + ex.Message, alerte: true);
            }
            finally
            {
                _basculeEnCours = false;
            }

            await Task.Delay(700);
            MetAJourEtatService();

            CoreLog.Info("MediaMonitor", "Service : "
                                + (ServiceMediaEnCours ? "en cours" : "arrêté"));
        }

        private async Task<bool> DemarrerServiceMediaAsync()
        {
            if (!File.Exists(CheminServiceMedia))
            {
                BasculeService.IsChecked = false;
                AfficherMessage(LanguageManager.Get("MediaMonitor.Service.exe introuvable.")
                                ?? "MediaMonitor.Service.exe introuvable.", alerte: true);
                return false;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = CheminServiceMedia,
                UseShellExecute = true
            });

            // Le service a besoin d'un court instant pour apparaître dans la liste
            // des processus : on lui laisse le temps avant de conclure à un échec.
            await Task.Delay(1200);

            if (!ServiceMediaEnCours)
            {
                BasculeService.IsChecked = false;
                AfficherMessage("Le service MediaMonitor.Service n'a pas pu démarrer.", alerte: true);
                return false;
            }

            return true;
        }

        private static void DemarrerTrayMedia()
        {
            string cheminTray = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "MCEMonitor",
                "MediaMonitor.Tray.exe");

            if (!File.Exists(cheminTray))
            {
                cheminTray = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "MCEMonitor",
                    "MediaMonitor.Tray.exe");
            }

            if (File.Exists(cheminTray))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = cheminTray,
                    UseShellExecute = true
                });
            }
        }

        private async void SurAutomatisationMedia(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_tacheMediaExiste)
                {
                    var reponse = Boite.Show(this, 
                        LanguageManager.Get("Voulez-vous vraiment désactiver l'automatisation MediaMonitor ?")
                        ?? "Voulez-vous vraiment désactiver l'automatisation MediaMonitor ?",
                        LanguageManager.Get("Confirmation") ?? "Confirmation",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (reponse != MessageBoxResult.Yes)
                        return;

                    string resultat = await Task.Run(TaskSchedulerHelper.DeleteMediaMonitorServiceTask);
                    JournaliserTache("MediaMonitor", "MCEMonitor_MediaMonitorService", "suppression", resultat);
                    AfficherMessage(resultat);
                }
                else
                {
                    string resultat = await Task.Run(TaskSchedulerHelper.CreateMediaMonitorServiceTask);
                    JournaliserTache("MediaMonitor", "MCEMonitor_MediaMonitorService", "création", resultat);
                    AfficherMessage(resultat);
                }

                await RafraichirTacheMediaAsync();
            }
            catch (Exception ex)
            {
                AfficherMessage((LanguageManager.Get("Erreur : ") ?? "Erreur : ") + ex.Message, alerte: true);
            }
        }

        private async void SurOuvrirMediaUI(object sender, RoutedEventArgs e)
        {
            if (!ServiceMediaEnCours)
            {
                var reponse = Boite.Show(this, 
                    (LanguageManager.Get("Le service MediaMonitor n'est pas en cours d'exécution.")
                     ?? "Le service MediaMonitor n'est pas en cours d'exécution.")
                    + "\n\n" +
                    (LanguageManager.Get("Voulez-vous le démarrer maintenant ?")
                     ?? "Voulez-vous le démarrer maintenant ?"),
                    LanguageManager.Get("Service non démarré") ?? "Service non démarré",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (reponse != MessageBoxResult.Yes)
                    return;

                if (!await DemarrerServiceMediaAsync())
                    return;

                MetAJourEtatService();

                // Parité WinForms : le Tray démarre avec le service.
                DemarrerTrayMedia();
            }

            try
            {
                string cheminUI = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MediaMonitor.UI.exe");

                if (!File.Exists(cheminUI))
                {
                    AfficherMessage("MediaMonitor.UI.exe est introuvable dans le dossier de MCEMonitor.", alerte: true);
                    return;
                }

                string langue = LanguageManager.CurrentLanguage ?? "fr-FR";

                Process.Start(new ProcessStartInfo
                {
                    FileName = cheminUI,
                    Arguments = $"--from-mcem -lang {langue}",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AfficherMessage("Erreur lors de l'ouverture de MediaMonitor.UI : " + ex.Message, alerte: true);
            }
        }

        // ============================================================
        //  Rapports planifiés de MediaMonitor
        // ============================================================

        private static string FichierPlanification => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "Logs",
            "MediaMonitor.Schedule.log");

        private void MetAJourLibellesRapports()
        {
            AppliquerLibelleRapport(texteProchain: LireDernierStatut("[CODE01]"), champ: TexteProchainRapport);
            AppliquerLibelleRapport(texteProchain: LireDernierStatut("[CODE02]"), champ: TexteDernierRapport);
        }

        // Une ligne sans contenu est masquée : sinon elle réserve une bande vide
        // entre l'état du service et les boutons.
        private static void AppliquerLibelleRapport(string texteProchain, System.Windows.Controls.TextBlock champ)
        {
            if (string.IsNullOrWhiteSpace(texteProchain))
            {
                champ.Text = string.Empty;
                champ.Visibility = Visibility.Collapsed;
                return;
            }

            champ.Text = texteProchain;
            champ.Visibility = Visibility.Visible;
        }

        private static string LireDernierStatut(string marqueur)
        {
            try
            {
                if (!File.Exists(FichierPlanification))
                    return string.Empty;

                string? ligne = File.ReadLines(FichierPlanification)
                    .Reverse()
                    .FirstOrDefault(l => l.Contains(marqueur));

                if (string.IsNullOrWhiteSpace(ligne))
                    return string.Empty;

                int idx = ligne.IndexOf("] ");
                if (idx > 0)
                    ligne = ligne.Substring(idx + 2);

                return TraduireCodeStatut(ligne);
            }
            catch
            {
                return string.Empty;
            }
        }

        // Les lignes du journal portent un code machine du genre [CODE01]|12:30|2h ;
        // on les rend lisibles, dans la langue de l'interface.
        private static string TraduireCodeStatut(string ligne)
        {
            if (string.IsNullOrWhiteSpace(ligne))
                return string.Empty;

            if (ligne.StartsWith("[CODE01]|"))
            {
                var parties = ligne.Substring("[CODE01]|".Length).Split('|');
                string heure = parties.Length > 0 ? parties[0] : "";
                string dans  = parties.Length > 1 ? parties[1] : "";

                string libelle = LanguageManager.Get("Prochain envoi du rapport prévu à")
                                 ?? "Prochain envoi du rapport prévu à";
                string libelleDans = LanguageManager.Get("dans") ?? "dans";

                return $"{libelle} {heure} ({libelleDans} {dans})";
            }

            if (ligne.StartsWith("[CODE02]|"))
            {
                string valeur = ligne.Substring("[CODE02]|".Length).Trim();

                if (valeur == "AUCUN")
                    return LanguageManager.Get("Aucun rapport envoyé") ?? "Aucun rapport envoyé";

                return (LanguageManager.Get("Rapport envoyé à") ?? "Rapport envoyé à") + " " + valeur;
            }

            return ligne;
        }

        /// <summary>
        /// Trace une creation ou une suppression de tache planifiee, avec le retour
        /// de schtasks. Les retours multiples (StopMonitor) sont aplatis sur une ligne.
        /// </summary>
        private static void JournaliserTache(
            string domaine, string nomTache, string action, string resultat)
        {
            string detail = resultat.Replace('\r', ' ')
                                    .Replace('\n', ' ')
                                    .Trim();

            string message = $"Tâche {nomTache} : {action} -> {detail}";

            if (resultat.Contains("ERREUR", StringComparison.OrdinalIgnoreCase))
                CoreLog.Warn(domaine, message);
            else
                CoreLog.Info(domaine, message);
        }

        private void AfficherMessage(string message, bool alerte = false)
        {
            MessageMedia.Text = message;
            MessageMedia.Foreground = (Brush)FindResource(alerte ? "Erreur" : "TexteSecondaire");
            MessageMedia.Visibility = Visibility.Visible;
        }

        // ============================================================
        //  ONGLET ROMMONITOR
        // ============================================================

        private readonly DispatcherTimer _minuteurServiceRom = new() { Interval = TimeSpan.FromSeconds(3) };

        private bool _basculeRomEnCours;
        private bool _tacheRomExiste;

        private static string CheminServiceRom => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "RomMonitor.Service.exe");

        private static string FichierConfigRom => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "RomMonitor.config");

        private static bool ServiceRomEnCours =>
            Process.GetProcessesByName("RomMonitor.Service").Length > 0;

        private static bool InterfaceRomOuverte =>
            Process.GetProcessesByName("RomMonitor.UI").Length > 0;

        private void ChargerRom()
        {
            MetAJourEtatServiceRom();
            MetAJourBoutonAutomatiqueRom();
            ChargerReglagesRom();

            if (!EstEleve())
            {
                MessageRom.Text = (LanguageManager.Get("Ui.NotElevatedService") ?? "Fenêtre non élevée : la gestion du service et des tâches planifiées sera refusée par Windows.");
                MessageRom.Foreground = (Brush)FindResource("Erreur");
                MessageRom.Visibility = Visibility.Visible;
            }

            _minuteurServiceRom.Tick += (s, e) => MetAJourEtatServiceRom();
            _minuteurServiceRom.Start();

            _ = RafraichirTacheRomAsync();
        }

        private void MetAJourEtatServiceRom()
        {
            if (_basculeRomEnCours)
                return;

            bool actif = ServiceRomEnCours;

            BasculeServiceRom.IsChecked = actif;

            TexteEtatServiceRom.Text = actif
                ? (LanguageManager.Get("Service RomMonitor : actif")  ?? "Service RomMonitor : actif")
                : (LanguageManager.Get("Service RomMonitor : arrêté") ?? "Service RomMonitor : arrêté");
        }

        private async Task RafraichirTacheRomAsync()
        {
            try
            {
                _tacheRomExiste = await Task.Run(TaskSchedulerHelper.RomMonitorTaskExists);
                MetAJourBoutonAutomatiqueRom();
            }
            catch { }
        }

        private void MetAJourBoutonAutomatiqueRom()
        {
            BoutonAutomatiqueRom.Content = _tacheRomExiste
                ? (LanguageManager.Get("Automatique ON")  ?? "Automatique ON")
                : (LanguageManager.Get("Automatique OFF") ?? "Automatique OFF");

            BoutonAutomatiqueRom.Style = (Style)FindResource(
                _tacheRomExiste ? "BoutonSucces" : "BoutonDanger");
        }

        private async void SurBasculeServiceRom(object sender, RoutedEventArgs e)
        {
            if (_basculeRomEnCours)
                return;

            _basculeRomEnCours = true;

            // Le message d'enregistrement décrit l'état du service au moment du clic :
            // il devient faux dès qu'on le démarre ou qu'on l'arrête.
            MessageRom.Visibility = Visibility.Collapsed;

            CoreLog.Info("RomMonitor", "Service : "
                                + (ServiceRomEnCours ? "arrêt demandé" : "démarrage demandé"));

            try
            {
                if (ServiceRomEnCours)
                {
                    if (InterfaceRomOuverte)
                    {
                        BasculeServiceRom.IsChecked = true;
                        MessageRom.Text = "Impossible d'arrêter RomMonitor.Service tant que RomMonitor.UI est ouvert.";
                        MessageRom.Foreground = (Brush)FindResource("Erreur");
                        MessageRom.Visibility = Visibility.Visible;
                        return;
                    }

                    foreach (var processus in Process.GetProcessesByName("RomMonitor.Service"))
                        processus.Kill();
                }
                else
                {
                    if (!File.Exists(CheminServiceRom))
                    {
                        BasculeServiceRom.IsChecked = false;
                        MessageRom.Text = "RomMonitor.Service.exe introuvable.";
                        MessageRom.Foreground = (Brush)FindResource("Erreur");
                        MessageRom.Visibility = Visibility.Visible;
                        return;
                    }

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = CheminServiceRom,
                        UseShellExecute = true
                    });

                    await Task.Delay(1200);

                    if (!ServiceRomEnCours)
                    {
                        BasculeServiceRom.IsChecked = false;
                        MessageRom.Text = "Le service RomMonitor.Service n'a pas pu démarrer.";
                        MessageRom.Foreground = (Brush)FindResource("Erreur");
                        MessageRom.Visibility = Visibility.Visible;
                        return;
                    }

                    DemarrerTray("RomMonitor.Tray.exe");
                }
            }
            catch (Exception ex)
            {
                MessageRom.Text = "Erreur : " + ex.Message;
                MessageRom.Foreground = (Brush)FindResource("Erreur");
                MessageRom.Visibility = Visibility.Visible;
            }
            finally
            {
                _basculeRomEnCours = false;
            }

            await Task.Delay(700);
            MetAJourEtatServiceRom();

            CoreLog.Info("RomMonitor", "Service : "
                                + (ServiceRomEnCours ? "en cours" : "arrêté"));
        }

        // Lance le Tray associé au service, dans Program Files ou Program Files (x86).
        private static void DemarrerTray(string nomTray)
        {
            try
            {
                // Comme dans l'ancienne interface : ne pas relancer un Tray qui tourne déjà.
                string nomProcessus = Path.GetFileNameWithoutExtension(nomTray);

                if (Process.GetProcessesByName(nomProcessus).Length > 0)
                    return;

                foreach (var racine in new[]
                         {
                             Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                             Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                         })
                {
                    string chemin = Path.Combine(racine, "MCEMonitor", nomTray);

                    if (File.Exists(chemin))
                    {
                        Process.Start(new ProcessStartInfo { FileName = chemin, UseShellExecute = true });
                        return;
                    }
                }
            }
            catch { }
        }

        private async void SurAutomatisationRom(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_tacheRomExiste)
                {
                    var reponse = Boite.Show(this, 
                        LanguageManager.Get("Voulez-vous vraiment désactiver l'automatisation RomMonitor ?")
                        ?? "Voulez-vous vraiment désactiver l'automatisation RomMonitor ?",
                        LanguageManager.Get("Confirmation") ?? "Confirmation",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (reponse != MessageBoxResult.Yes)
                        return;

                    string resultat = await Task.Run(TaskSchedulerHelper.DeleteRomMonitorTask);
                    // Parité WinForms : la tâche du Tray suit celle du service.
                    await Task.Run(TaskSchedulerHelper.DeleteRomTrayTask);
                    JournaliserTache("RomMonitor", "MCEMonitor_RomMonitorService", "suppression", resultat);
                    AfficherMessageRom(resultat);
                }
                else
                {
                    string resultat = await Task.Run(TaskSchedulerHelper.CreateRomMonitorTask);
                    // Parité WinForms : la tâche du Tray suit celle du service.
                    if (!TaskSchedulerHelper.RomTrayTaskExists())
                        await Task.Run(TaskSchedulerHelper.CreateRomTrayTask);
                    JournaliserTache("RomMonitor", "MCEMonitor_RomMonitorService", "création", resultat);
                    AfficherMessageRom(resultat);
                }

                await RafraichirTacheRomAsync();
            }
            catch (Exception ex)
            {
                AfficherMessageRom("Erreur : " + ex.Message);
            }
        }

        private void AfficherMessageRom(string message)
        {
            MessageRom.Text = message;
            MessageRom.Foreground = (Brush)FindResource(
                message.StartsWith("ERREUR", StringComparison.OrdinalIgnoreCase) ? "Erreur" : "TexteSecondaire");
            MessageRom.Visibility = Visibility.Visible;
        }

        private async void SurOuvrirRomUI(object sender, RoutedEventArgs e)
        {
            if (!ServiceRomEnCours)
            {
                var reponse = Boite.Show(this, 
                    (LanguageManager.Get("Le service RomMonitor n'est pas en cours d'exécution.")
                     ?? "Le service RomMonitor n'est pas en cours d'exécution.")
                    + "\n\n" +
                    (LanguageManager.Get("Voulez-vous le démarrer maintenant ?")
                     ?? "Voulez-vous le démarrer maintenant ?"),
                    LanguageManager.Get("Service non démarré") ?? "Service non démarré",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (reponse != MessageBoxResult.Yes)
                    return;

                if (!File.Exists(CheminServiceRom))
                {
                    AfficherMessageRom("RomMonitor.Service.exe introuvable.");
                    return;
                }

                Process.Start(new ProcessStartInfo { FileName = CheminServiceRom, UseShellExecute = true });

                await Task.Delay(1200);
                MetAJourEtatServiceRom();

                // Parité WinForms : le Tray démarre avec le service.
                DemarrerTray("RomMonitor.Tray.exe");
            }

            try
            {
                string cheminUI = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RomMonitor.UI.exe");

                if (!File.Exists(cheminUI))
                {
                    AfficherMessageRom("RomMonitor.UI.exe est introuvable dans le dossier de MCEMonitor.");
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName  = cheminUI,
                    Arguments = $"--from-mcem -lang {LanguageManager.CurrentLanguage ?? "fr-FR"}",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AfficherMessageRom("Erreur lors de l'ouverture de RomMonitor.UI : " + ex.Message);
            }
        }

        // ============================================================
        //  Réglages de RomMonitor
        // ============================================================

        private static Dictionary<string, string> LireConfigRom()
        {
            var valeurs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(FichierConfigRom))
                return valeurs;

            foreach (string ligne in File.ReadAllLines(FichierConfigRom))
            {
                if (string.IsNullOrWhiteSpace(ligne) || ligne.StartsWith("#", StringComparison.Ordinal))
                    continue;

                var parties = ligne.Split('=', 2);
                if (parties.Length != 2)
                    continue;

                valeurs[parties[0].Trim()] = parties[1].Trim();
            }

            return valeurs;
        }

        private void ChargerReglagesRom()
        {
            var config = LireConfigRom();

            ChampRomIntervalle.Text    = config.TryGetValue("Interval", out var intervalle) && int.TryParse(intervalle, out int i) ? i.ToString() : "60";
            ChampRomSeuilDanger.Text   = config.TryGetValue("DiskSpaceWarnPercent", out var danger) && int.TryParse(danger, out int d) ? d.ToString() : "10";
            ChampRomSeuilCritique.Text = config.TryGetValue("DiskSpaceCriticalPercent", out var critique) && int.TryParse(critique, out int c) ? c.ToString() : "5";
            ChampRomDelai.Text         = config.TryGetValue("AlertCooldownHours", out var delai) && int.TryParse(delai, out int h) ? h.ToString() : "24";

            CaseRomSmart.IsChecked = !config.TryGetValue("AlertOnSmartFailure", out var smart)
                                     || !smart.Equals("false", StringComparison.OrdinalIgnoreCase);

            ChampRomIoSeuil.Text      = config.TryGetValue("DiskIoWarnPercent", out var ioSeuil) && int.TryParse(ioSeuil, out int s1) ? s1.ToString() : "80";
            ChampRomIoCritique.Text   = config.TryGetValue("DiskIoCriticalPercent", out var ioCrit) && int.TryParse(ioCrit, out int s2) ? s2.ToString() : "95";
            ChampRomIoReleves.Text    = config.TryGetValue("DiskIoConfirmTicks", out var ioRel) && int.TryParse(ioRel, out int s3) ? s3.ToString() : "6";
            ChampRomIoIntervalle.Text = config.TryGetValue("DiskIoIntervalSeconds", out var ioMes) && int.TryParse(ioMes, out int s4) ? s4.ToString() : "10";

            CaseRomIo.IsChecked = !config.TryGetValue("AlertOnDiskIo", out var ioAlert)
                                  || !ioAlert.Equals("false", StringComparison.OrdinalIgnoreCase);
        }

        private async void SurEnregistrerReglagesRom(object sender, RoutedEventArgs e)
        {
            try
            {
                // Bornes reprises des contrôles WinForms d'origine
                int intervalle = Borner(ChampRomIntervalle.Text,    15, 1440, 60);
                int danger     = Borner(ChampRomSeuilDanger.Text,    5,  100, 10);
                int critique   = Borner(ChampRomSeuilCritique.Text,  1,  100,  5);
                int delai      = Borner(ChampRomDelai.Text,          1,  168, 24);

                int ioSeuil    = Borner(ChampRomIoSeuil.Text,       10, 100, 80);
                int ioCritique = Borner(ChampRomIoCritique.Text,    20, 100, 95);
                int ioReleves  = Borner(ChampRomIoReleves.Text,      1,  60,  6);
                int ioMesure   = Borner(ChampRomIoIntervalle.Text,   5, 300, 10);

                if (ioCritique <= ioSeuil)
                {
                    ioCritique = Math.Min(100, ioSeuil + 5);
                    ChampRomIoCritique.Text = ioCritique.ToString();
                }

                // Les réglages du serveur web appartiennent à RomMonitor.UI :
                // on les relit pour les réécrire à l'identique.
                var config = LireConfigRom();
                string webEnabled  = config.TryGetValue("WebEnabled",  out var we) ? we : "true";
                string webPort     = config.TryGetValue("WebPort",     out var wp) ? wp : "8085";
                string webUser     = config.TryGetValue("WebUsername", out var wu) ? wu : "admin";
                string webPassword = config.TryGetValue("WebPassword", out var wm) ? wm : "changeme";

                // Seuils en Go : réglés uniquement dans RomMonitor.config
                string warnGo = config.TryGetValue("DiskSpaceWarnGo",     out var wg) ? wg : "10";
                string critGo = config.TryGetValue("DiskSpaceCriticalGo", out var cg) ? cg : "5";

                Directory.CreateDirectory(Path.GetDirectoryName(FichierConfigRom)!);

                var lignes = new[]
                {
                    "# ============================================================",
                    "# RomMonitor.config",
                    "# Configuration du service RomMonitor",
                    "# ============================================================",
                    "",
                    "# Fréquence de contrôle (minutes)",
                    $"Interval={intervalle}",
                    "",
                    "# Seuil d'alerte espace disque (% libre)",
                    $"DiskSpaceWarnPercent={danger}",
                    $"DiskSpaceCriticalPercent={critique}",
                    "",
                    "# Seuil d'alerte espace disque (Go libre)",
                    $"DiskSpaceWarnGo={warnGo}",
                    $"DiskSpaceCriticalGo={critGo}",
                    "",
                    "# Alertes email",
                    $"AlertOnSmartFailure={(CaseRomSmart.IsChecked == true ? "true" : "false")}",
                    "AlertOnLowDiskSpace=true",
                    "",
                    "# Anti-spam : délai minimum entre 2 alertes email du même type (heures)",
                    $"AlertCooldownHours={delai}",
                    "",
                    "# Surcharge disque : alerte si un disque reste occupe au-dela du seuil",
                    $"AlertOnDiskIo={(CaseRomIo.IsChecked == true).ToString().ToLowerInvariant()}",
                    $"DiskIoWarnPercent={ioSeuil}",
                    $"DiskIoCriticalPercent={ioCritique}",
                    $"DiskIoConfirmTicks={ioReleves}",
                    $"DiskIoIntervalSeconds={ioMesure}",
                    "",
                    "# Serveur Web",
                    $"WebEnabled={webEnabled}",
                    $"WebPort={webPort}",
                    $"WebUsername={webUser}",
                    $"WebPassword={webPassword}"
                };

                File.WriteAllLines(FichierConfigRom, lignes);

                CoreLog.Info("RomMonitor", "Réglages enregistrés");

                bool recharge = await ServiceReload.RechargerAsync(ServiceReload.PipeRom);

                if (recharge)
                {
                    CoreLog.Info("RomMonitor", "Service rechargé à chaud");
                    AfficherMessageRom(LanguageManager.Get("Réglages enregistrés, service rechargé.")
                                       ?? "Réglages enregistrés, service rechargé.");
                }
                else if (!ServiceRomEnCours)
                {
                    CoreLog.Info("RomMonitor", "Service arrêté : réglages appliqués à son démarrage");
                    AfficherMessageRom(LanguageManager.Get("Réglages enregistrés. Le service n'est pas démarré : ils seront appliqués à son démarrage.")
                                       ?? "Réglages enregistrés. Le service n'est pas démarré : ils seront appliqués à son démarrage.");
                }
                else
                {
                    CoreLog.Warn("RomMonitor", "Service non notifié : redémarrage nécessaire");
                    AfficherMessageRom(LanguageManager.Get("Réglages enregistrés, mais le service n'a pas pu être notifié : redémarrez-le.")
                                       ?? "Réglages enregistrés, mais le service n'a pas pu être notifié : redémarrez-le.");
                }
            }
            catch (Exception ex)
            {
                AfficherMessageRom("Erreur lors de l'enregistrement : " + ex.Message);
            }
        }

        private static int Borner(string saisie, int minimum, int maximum, int defaut)
        {
            if (!int.TryParse(saisie, out int valeur))
                return defaut;

            return Math.Max(minimum, Math.Min(maximum, valeur));
        }

        // ============================================================
        //  ONGLET SYSTEMMONITOR
        // ============================================================

        private readonly DispatcherTimer _minuteurServiceSystem = new() { Interval = TimeSpan.FromSeconds(3) };

        private bool _basculeSystemEnCours;
        private bool _tacheSystemExiste;

        private static string CheminServiceSystem => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "SystemMonitor.Service.exe");

        private static string FichierConfigSystem => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "SystemMonitor.config");

        private static bool ServiceSystemEnCours =>
            Process.GetProcessesByName("SystemMonitor.Service").Length > 0;

        private static bool InterfaceSystemOuverte =>
            Process.GetProcessesByName("SystemMonitor.UI").Length > 0;

        private void ChargerSystem()
        {
            MetAJourEtatServiceSystem();
            MetAJourBoutonAutomatiqueSystem();
            ChargerReglagesSystem();

            if (!EstEleve())
            {
                MessageSystem.Text = (LanguageManager.Get("Ui.NotElevatedService") ?? "Fenêtre non élevée : la gestion du service et des tâches planifiées sera refusée par Windows.");
                MessageSystem.Foreground = (Brush)FindResource("Erreur");
                MessageSystem.Visibility = Visibility.Visible;
            }

            _minuteurServiceSystem.Tick += (s, e) => MetAJourEtatServiceSystem();
            _minuteurServiceSystem.Start();

            _ = RafraichirTacheSystemAsync();
        }

        private void MetAJourEtatServiceSystem()
        {
            if (_basculeSystemEnCours)
                return;

            bool actif = ServiceSystemEnCours;

            BasculeServiceSystem.IsChecked = actif;

            TexteEtatServiceSystem.Text = actif
                ? (LanguageManager.Get("Service SystemMonitor : actif")  ?? "Service SystemMonitor : actif")
                : (LanguageManager.Get("Service SystemMonitor : arrêté") ?? "Service SystemMonitor : arrêté");
        }

        private async Task RafraichirTacheSystemAsync()
        {
            try
            {
                _tacheSystemExiste = await Task.Run(TaskSchedulerHelper.SystemMonitorTaskExists);
                MetAJourBoutonAutomatiqueSystem();
            }
            catch { }
        }

        private void MetAJourBoutonAutomatiqueSystem()
        {
            BoutonAutomatiqueSystem.Content = _tacheSystemExiste
                ? (LanguageManager.Get("Automatique ON")  ?? "Automatique ON")
                : (LanguageManager.Get("Automatique OFF") ?? "Automatique OFF");

            BoutonAutomatiqueSystem.Style = (Style)FindResource(
                _tacheSystemExiste ? "BoutonSucces" : "BoutonDanger");
        }

        private async void SurBasculeServiceSystem(object sender, RoutedEventArgs e)
        {
            if (_basculeSystemEnCours)
                return;

            _basculeSystemEnCours = true;

            MessageSystem.Visibility = Visibility.Collapsed;

            CoreLog.Info("SystemMonitor", "Service : "
                                + (ServiceSystemEnCours ? "arrêt demandé" : "démarrage demandé"));

            try
            {
                if (ServiceSystemEnCours)
                {
                    if (InterfaceSystemOuverte)
                    {
                        BasculeServiceSystem.IsChecked = true;
                        AfficherMessageSystem("Impossible d'arrêter SystemMonitor.Service tant que SystemMonitor.UI est ouvert.", alerte: true);
                        return;
                    }

                    foreach (var processus in Process.GetProcessesByName("SystemMonitor.Service"))
                        processus.Kill();
                }
                else
                {
                    if (!File.Exists(CheminServiceSystem))
                    {
                        BasculeServiceSystem.IsChecked = false;
                        AfficherMessageSystem("SystemMonitor.Service.exe introuvable.", alerte: true);
                        return;
                    }

                    Process.Start(new ProcessStartInfo { FileName = CheminServiceSystem, UseShellExecute = true });

                    await Task.Delay(1200);

                    if (!ServiceSystemEnCours)
                    {
                        BasculeServiceSystem.IsChecked = false;
                        AfficherMessageSystem("Le service SystemMonitor.Service n'a pas pu démarrer.", alerte: true);
                        return;
                    }

                    DemarrerTray("SystemMonitor.Tray.exe");
                }
            }
            catch (Exception ex)
            {
                AfficherMessageSystem("Erreur : " + ex.Message, alerte: true);
            }
            finally
            {
                _basculeSystemEnCours = false;
            }

            await Task.Delay(700);
            MetAJourEtatServiceSystem();

            CoreLog.Info("SystemMonitor", "Service : "
                                + (ServiceSystemEnCours ? "en cours" : "arrêté"));
        }

        private async void SurAutomatisationSystem(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_tacheSystemExiste)
                {
                    var reponse = Boite.Show(this, 
                        LanguageManager.Get("Voulez-vous vraiment désactiver l'automatisation SystemMonitor ?")
                        ?? "Voulez-vous vraiment désactiver l'automatisation SystemMonitor ?",
                        LanguageManager.Get("Confirmation") ?? "Confirmation",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (reponse != MessageBoxResult.Yes)
                        return;

                    string resultat = await Task.Run(TaskSchedulerHelper.DeleteSystemMonitorTask);
                    // Parité WinForms : la tâche du Tray suit celle du service.
                    await Task.Run(TaskSchedulerHelper.DeleteSystemTrayTask);
                    JournaliserTache("SystemMonitor", "MCEMonitor_SystemMonitorService", "suppression", resultat);
                    AfficherMessageSystem(resultat);
                }
                else
                {
                    string resultat = await Task.Run(TaskSchedulerHelper.CreateSystemMonitorTask);
                    // Parité WinForms : la tâche du Tray suit celle du service.
                    if (!TaskSchedulerHelper.SystemTrayTaskExists())
                        await Task.Run(TaskSchedulerHelper.CreateSystemTrayTask);
                    JournaliserTache("SystemMonitor", "MCEMonitor_SystemMonitorService", "création", resultat);
                    AfficherMessageSystem(resultat);
                }

                await RafraichirTacheSystemAsync();
            }
            catch (Exception ex)
            {
                AfficherMessageSystem("Erreur : " + ex.Message, alerte: true);
            }
        }

        private void AfficherMessageSystem(string message, bool alerte = false)
        {
            MessageSystem.Text = message;
            MessageSystem.Foreground = (Brush)FindResource(alerte ? "Erreur" : "TexteSecondaire");
            MessageSystem.Visibility = Visibility.Visible;
        }

        private async void SurOuvrirSystemUI(object sender, RoutedEventArgs e)
        {
            if (!ServiceSystemEnCours)
            {
                var reponse = Boite.Show(this, 
                    (LanguageManager.Get("Le service SystemMonitor n'est pas en cours d'exécution.")
                     ?? "Le service SystemMonitor n'est pas en cours d'exécution.")
                    + "\n\n" +
                    (LanguageManager.Get("Voulez-vous le démarrer maintenant ?")
                     ?? "Voulez-vous le démarrer maintenant ?"),
                    LanguageManager.Get("Service non démarré") ?? "Service non démarré",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (reponse != MessageBoxResult.Yes)
                    return;

                if (!File.Exists(CheminServiceSystem))
                {
                    AfficherMessageSystem("SystemMonitor.Service.exe introuvable.", alerte: true);
                    return;
                }

                Process.Start(new ProcessStartInfo { FileName = CheminServiceSystem, UseShellExecute = true });

                await Task.Delay(1200);
                MetAJourEtatServiceSystem();

                // Parité WinForms : le Tray démarre avec le service.
                DemarrerTray("SystemMonitor.Tray.exe");
            }

            try
            {
                string cheminUI = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SystemMonitor.UI.exe");

                if (!File.Exists(cheminUI))
                {
                    AfficherMessageSystem("SystemMonitor.UI.exe est introuvable dans le dossier de MCEMonitor.", alerte: true);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName  = cheminUI,
                    Arguments = $"--from-mcem -lang {LanguageManager.CurrentLanguage ?? "fr-FR"}",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AfficherMessageSystem("Erreur lors de l'ouverture de SystemMonitor.UI : " + ex.Message, alerte: true);
            }
        }

        // ============================================================
        //  Réglages de SystemMonitor
        // ============================================================

        private static Dictionary<string, string> LireConfigSystem()
        {
            var valeurs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(FichierConfigSystem))
                return valeurs;

            foreach (string ligne in File.ReadAllLines(FichierConfigSystem))
            {
                if (string.IsNullOrWhiteSpace(ligne) || ligne.StartsWith("#", StringComparison.Ordinal))
                    continue;

                var parties = ligne.Split('=', 2);
                if (parties.Length != 2)
                    continue;

                valeurs[parties[0].Trim()] = parties[1].Trim();
            }

            return valeurs;
        }

        private void ChargerReglagesSystem()
        {
            var config = LireConfigSystem();

            ChampSystemIntervalle.Text = Valeur(config, "Interval", "5");
            ChampSystemDelai.Text      = Valeur(config, "CpuCooldownMinutes", "15");
            ChampSystemCpu.Text        = Valeur(config, "CpuThresholdPercent", "85");
            ChampSystemRam.Text        = Valeur(config, "RamThresholdPercent", "85");
            ChampSystemTemp.Text       = Valeur(config, "TempThresholdCelsius", "85");
            ChampSystemGpuTemp.Text    = Valeur(config, "GpuTempThresholdCelsius", "85");

            CaseSystemCpu.IsChecked     = EstVrai(config, "AlertOnHighCpu");
            CaseSystemRam.IsChecked     = EstVrai(config, "AlertOnHighRam");
            CaseSystemTemp.IsChecked    = EstVrai(config, "AlertOnHighTemp");
            CaseSystemGpuTemp.IsChecked = EstVrai(config, "AlertOnHighGpuTemp");
        }

        private static string Valeur(Dictionary<string, string> config, string cle, string defaut)
            => config.TryGetValue(cle, out var valeur) ? valeur : defaut;

        // Une clé absente vaut « activé » : c'est le comportement de la version WinForms,
        // qui laisse la case cochée par défaut.
        private static bool EstVrai(Dictionary<string, string> config, string cle)
            => !config.TryGetValue(cle, out var valeur)
               || !valeur.Equals("false", StringComparison.OrdinalIgnoreCase);

        // Variante pour les clés dont le défaut du service est « faux » : sans elle,
        // une clé absente du fichier afficherait une coche que le service n'applique pas.
        private static bool EstVrai(Dictionary<string, string> config, string cle, bool defaut)
            => config.TryGetValue(cle, out var valeur)
                ? !valeur.Equals("false", StringComparison.OrdinalIgnoreCase)
                : defaut;

        private async void SurEnregistrerReglagesSystem(object sender, RoutedEventArgs e)
        {
            try
            {
                int intervalle = Borner(ChampSystemIntervalle.Text, 1, 60, 5);
                int delai      = Borner(ChampSystemDelai.Text,      1, 1440, 15);
                int cpu        = Borner(ChampSystemCpu.Text,       10, 100, 85);
                int ram        = Borner(ChampSystemRam.Text,       10, 100, 85);
                int temp       = Borner(ChampSystemTemp.Text,      40, 110, 85);
                int gpuTemp    = Borner(ChampSystemGpuTemp.Text,   40, 110, 85);

                var config = LireConfigSystem();

                Directory.CreateDirectory(Path.GetDirectoryName(FichierConfigSystem)!);

                var lignes = new[]
                {
                    "# ============================================================",
                    "# SystemMonitor.config",
                    "# Configuration du service SystemMonitor",
                    "# ============================================================",
                    "",
                    "# Fréquence de rafraîchissement (secondes)",
                    $"Interval={intervalle}",
                    "",
                    "# Alertes CPU",
                    $"AlertOnHighCpu={(CaseSystemCpu.IsChecked == true ? "true" : "false")}",
                    $"CpuThresholdPercent={cpu}",
                    $"CpuCooldownMinutes={delai}",
                    "",
                    "# Alertes RAM",
                    $"AlertOnHighRam={(CaseSystemRam.IsChecked == true ? "true" : "false")}",
                    $"RamThresholdPercent={ram}",
                    "",
                    "# Alertes Température CPU",
                    $"AlertOnHighTemp={(CaseSystemTemp.IsChecked == true ? "true" : "false")}",
                    $"TempThresholdCelsius={temp}",
                    "",
                    "# Alertes Température GPU",
                    $"AlertOnHighGpuTemp={(CaseSystemGpuTemp.IsChecked == true ? "true" : "false")}",
                    $"GpuTempThresholdCelsius={gpuTemp}",
                    "",
                    "# Serveur Web",
                    $"WebEnabled={Valeur(config, "WebEnabled", "true")}",
                    $"WebPort={Valeur(config, "WebPort", "8083")}",
                    $"WebUsername={Valeur(config, "WebUsername", "admin")}",
                    $"WebPassword={Valeur(config, "WebPassword", "changeme")}"
                };

                File.WriteAllLines(FichierConfigSystem, lignes);

                CoreLog.Info("SystemMonitor", "Réglages enregistrés");

                bool recharge = await ServiceReload.RechargerAsync(ServiceReload.PipeSystem);

                if (recharge)
                {
                    CoreLog.Info("SystemMonitor", "Service rechargé à chaud");
                    AfficherMessageSystem(LanguageManager.Get("Réglages enregistrés, service rechargé.")
                                       ?? "Réglages enregistrés, service rechargé.");
                }
                else if (!ServiceSystemEnCours)
                {
                    CoreLog.Info("SystemMonitor", "Service arrêté : réglages appliqués à son démarrage");
                    AfficherMessageSystem(LanguageManager.Get("Réglages enregistrés. Le service n'est pas démarré : ils seront appliqués à son démarrage.")
                                       ?? "Réglages enregistrés. Le service n'est pas démarré : ils seront appliqués à son démarrage.");
                }
                else
                {
                    CoreLog.Warn("SystemMonitor", "Service non notifié : redémarrage nécessaire");
                    AfficherMessageSystem(LanguageManager.Get("Réglages enregistrés, mais le service n'a pas pu être notifié : redémarrez-le.")
                                       ?? "Réglages enregistrés, mais le service n'a pas pu être notifié : redémarrez-le.");
                }
            }
            catch (Exception ex)
            {
                AfficherMessageSystem("Erreur lors de l'enregistrement : " + ex.Message, alerte: true);
            }
        }

        // ============================================================
        //  ONGLET WAKEMONITOR
        // ============================================================

        private bool _tacheWakeExiste;

        private static string FichierConfigWake => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "WakeMonitor.config");

        private void ChargerWake()
        {
            MetAJourBoutonAutomatiqueWake();
            ChargerReglagesWake();

            if (!EstEleve())
            {
                MessageWake.Text = (LanguageManager.Get("Ui.NotElevatedTasks") ?? "Fenêtre non élevée : la création et la lecture des tâches planifiées seront refusées par Windows.");
                MessageWake.Foreground = (Brush)FindResource("Erreur");
                MessageWake.Visibility = Visibility.Visible;
            }

            _ = RafraichirTacheWakeAsync();
        }

        private async Task RafraichirTacheWakeAsync()
        {
            try
            {
                _tacheWakeExiste = await Task.Run(TaskSchedulerHelper.WakeTaskExists);
                MetAJourBoutonAutomatiqueWake();
            }
            catch { }
        }

        private void MetAJourBoutonAutomatiqueWake()
        {
            BoutonAutomatiqueWake.Content = _tacheWakeExiste
                ? (LanguageManager.Get("Automatique ON")  ?? "Automatique ON")
                : (LanguageManager.Get("Automatique OFF") ?? "Automatique OFF");

            BoutonAutomatiqueWake.Style = (Style)FindResource(
                _tacheWakeExiste ? "BoutonSucces" : "BoutonDanger");
        }

        private async void SurAutomatisationWake(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_tacheWakeExiste)
                {
                    var reponse = Boite.Show(this, 
                        LanguageManager.Get("Voulez-vous vraiment désactiver l'automatisation WakeMonitor ?")
                        ?? "Voulez-vous vraiment désactiver l'automatisation WakeMonitor ?",
                        LanguageManager.Get("Confirmation") ?? "Confirmation",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (reponse != MessageBoxResult.Yes)
                        return;

                    string resultat = await Task.Run(TaskSchedulerHelper.DeleteWakeTask);
                    JournaliserTache("WakeMonitor", "MCEMonitor_WakeMonitor", "suppression", resultat);
                    AfficherMessageWake(resultat);
                }
                else
                {
                    string resultat = await Task.Run(TaskSchedulerHelper.CreateWakeTask);
                    JournaliserTache("WakeMonitor", "MCEMonitor_WakeMonitor", "création", resultat);
                    AfficherMessageWake(resultat);
                }

                await RafraichirTacheWakeAsync();
            }
            catch (Exception ex)
            {
                AfficherMessageWake("Erreur : " + ex.Message, alerte: true);
            }
        }

        private void AfficherMessageWake(string message, bool alerte = false)
        {
            MessageWake.Text = message;
            MessageWake.Foreground = (Brush)FindResource(alerte ? "Erreur" : "TexteSecondaire");
            MessageWake.Visibility = Visibility.Visible;
        }

        // WakeMonitor.exe ne produit un mail que si le dernier événement système est un vrai
        // réveil : lancé à froid, il sort silencieusement. Un bouton nommé « mail de test »
        // doit toujours envoyer quelque chose, sinon on ne sait jamais s'il a fonctionné.
        // Le rapport réel, lui, continue d'être produit par la tâche planifiée.
        private async void SurMailTestWake(object sender, RoutedEventArgs e)
        {
            BoutonMailTestWake.IsEnabled = false;
            AfficherMessageWake(LanguageManager.Get("Ui.TestSending") ?? "Envoi du mail de test en cours…");

            try
            {
                var cfg = EmailConfig.Load();

                await EmailSender.SendAsync(
                    cfg,
                    LanguageManager.Get("Test SMTP WakeMonitor") ?? "Test SMTP WakeMonitor",
                    "<b>Ceci est un mail de test envoyé par MCEMonitor.</b><br><br>" +
                    "Si vous le recevez, la configuration d'envoi est correcte pour WakeMonitor. " +
                    "Le rapport de réveil réel, lui, est envoyé par la tâche planifiée au prochain réveil.",
                    isHtml: true);

                AfficherMessageWake(LanguageManager.Get("Ui.TestSent") ?? "Mail de test envoyé.");
            }
            catch (Exception ex)
            {
                AfficherMessageWake((LanguageManager.Get("Ui.SendFailed") ?? "Échec de l'envoi : ") + ex.Message, alerte: true);
            }
            finally
            {
                BoutonMailTestWake.IsEnabled = true;
            }
        }

        private void SurGererMacsWake(object sender, RoutedEventArgs e)
        {
            try
            {
                var fenetre = new FenetreMacsWol { Owner = this };
                fenetre.ShowDialog();
            }
            catch (Exception ex)
            {
                AfficherMessageWake("Impossible d'ouvrir la gestion des MAC autorisées : " + ex.Message, alerte: true);
            }
        }

        // ============================================================
        //  Réglages de WakeMonitor
        // ============================================================

        private void ChargerReglagesWake()
        {
            if (!File.Exists(FichierConfigWake))
                return;

            foreach (string ligne in File.ReadAllLines(FichierConfigWake))
            {
                if (!ligne.Contains('='))
                    continue;

                var parties = ligne.Split('=');
                string cle = parties[0].Trim();
                bool valeur = parties.Length > 1 && parties[1].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

                switch (cle)
                {
                    case "IncludePublicIP": CaseWakeIpPublique.IsChecked = valeur; break;
                    case "IncludeLocalIP":  CaseWakeIpLocale.IsChecked   = valeur; break;
                    case "IncludeMAC":      CaseWakeMac.IsChecked        = valeur; break;
                    case "IncludeUSB":      CaseWakeUsb.IsChecked        = valeur; break;
                    case "IncludeCause":    CaseWakeCause.IsChecked      = valeur; break;
                    case "IncludeDuration": CaseWakeDuree.IsChecked      = valeur; break;
                }
            }
        }

        private void SurEnregistrerWake(object sender, RoutedEventArgs e)
        {
            try
            {
                var lignes = new[]
                {
                    $"IncludePublicIP={(CaseWakeIpPublique.IsChecked == true ? "True" : "False")}",
                    $"IncludeLocalIP={(CaseWakeIpLocale.IsChecked == true ? "True" : "False")}",
                    $"IncludeMAC={(CaseWakeMac.IsChecked == true ? "True" : "False")}",
                    $"IncludeUSB={(CaseWakeUsb.IsChecked == true ? "True" : "False")}",
                    $"IncludeCause={(CaseWakeCause.IsChecked == true ? "True" : "False")}",
                    $"IncludeDuration={(CaseWakeDuree.IsChecked == true ? "True" : "False")}"
                };

                Directory.CreateDirectory(Path.GetDirectoryName(FichierConfigWake)!);
                File.WriteAllLines(FichierConfigWake, lignes);

                CoreLog.Info("WakeMonitor", "Réglages enregistrés");

                AfficherMessageWake(LanguageManager.Get("Configuration WakeMonitor enregistrée")
                                    ?? "Configuration WakeMonitor enregistrée");
            }
            catch (Exception ex)
            {
                AfficherMessageWake("Erreur lors de l'enregistrement : " + ex.Message, alerte: true);
            }
        }

        // ============================================================
        //  ONGLET STOPMONITOR
        // ============================================================

        private bool _tacheStopExiste;

        private void ChargerStop()
        {
            MetAJourBoutonAutomatiqueStop();

            if (!EstEleve())
            {
                AfficherMessageStop((LanguageManager.Get("Ui.NotElevatedTasks") ?? "Fenêtre non élevée : la création et la lecture des tâches planifiées seront refusées par Windows."), alerte: true);
            }

            _ = RafraichirTacheStopAsync();
        }

        private async Task RafraichirTacheStopAsync()
        {
            try
            {
                _tacheStopExiste = await Task.Run(TaskSchedulerHelper.StopTaskExists);
                MetAJourBoutonAutomatiqueStop();
            }
            catch { }
        }

        private void MetAJourBoutonAutomatiqueStop()
        {
            BoutonAutomatiqueStop.Content = _tacheStopExiste
                ? (LanguageManager.Get("Automatique ON")  ?? "Automatique ON")
                : (LanguageManager.Get("Automatique OFF") ?? "Automatique OFF");

            BoutonAutomatiqueStop.Style = (Style)FindResource(
                _tacheStopExiste ? "BoutonSucces" : "BoutonDanger");
        }

        private async void SurAutomatisationStop(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_tacheStopExiste)
                {
                    var reponse = Boite.Show(this, 
                        LanguageManager.Get("Voulez-vous vraiment désactiver l'automatisation StopMonitor ?")
                        ?? "Voulez-vous vraiment désactiver l'automatisation StopMonitor ?",
                        LanguageManager.Get("Confirmation") ?? "Confirmation",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (reponse != MessageBoxResult.Yes)
                        return;

                    string resultat = await Task.Run(TaskSchedulerHelper.DeleteStopTask);
                    JournaliserTache("StopMonitor", "MCEMonitor_StopMonitor", "suppression", resultat);
                    AfficherMessageStop(resultat);
                }
                else
                {
                    string resultat = await Task.Run(TaskSchedulerHelper.CreateStopTask);
                    JournaliserTache("StopMonitor", "MCEMonitor_StopMonitor", "création", resultat);
                    AfficherMessageStop(resultat);
                }

                await RafraichirTacheStopAsync();
            }
            catch (Exception ex)
            {
                AfficherMessageStop("Erreur : " + ex.Message, alerte: true);
            }
        }

        private void AfficherMessageStop(string message, bool alerte = false)
        {
            MessageStop.Text = message;
            MessageStop.Foreground = (Brush)FindResource(alerte ? "Erreur" : "TexteSecondaire");
            MessageStop.Visibility = Visibility.Visible;
        }

        // StopMonitor.exe sans argument n'envoie aucun mail : il n'analyse qu'en mode
        // « shutdown » ou « boot ». On envoie donc ici un vrai test SMTP, comme le fait
        // l'onglet Email, plutôt que de lancer un exécutable qui ne fera rien.
        private async void SurMailTestStop(object sender, RoutedEventArgs e)
        {
            BoutonMailTestStop.IsEnabled = false;
            AfficherMessageStop(LanguageManager.Get("Ui.TestSending") ?? "Envoi du mail de test en cours…");

            try
            {
                var cfg = EmailConfig.Load();

                await EmailSender.SendAsync(
                    cfg,
                    LanguageManager.Get("Test SMTP StopMonitor") ?? "Test SMTP StopMonitor",
                    "<b>Ceci est un mail de test envoyé par MCEMonitor.</b><br><br>" +
                    "Si vous le recevez, la configuration d'envoi est correcte pour StopMonitor.",
                    isHtml: true);

                AfficherMessageStop(LanguageManager.Get("Ui.TestSent") ?? "Mail de test envoyé.");
            }
            catch (Exception ex)
            {
                AfficherMessageStop((LanguageManager.Get("Ui.SendFailed") ?? "Échec de l'envoi : ") + ex.Message, alerte: true);
            }
            finally
            {
                BoutonMailTestStop.IsEnabled = true;
            }
        }

        // ============================================================
        //  ONGLET ON/OFF — arrêt programmé
        // ============================================================

        private const string ModeArret  = "shutdown";
        private const string ModeVeille = "sleep";

        private bool _tacheArretExiste;

        private static string FichierConfigArret => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "Shutdown.config");

        private void ChargerOnOff()
        {
            ChargerConfigArret();
            MetAJourBoutonAutomatiqueArret();

            if (!EstEleve())
            {
                AfficherMessageOnOff((LanguageManager.Get("Ui.NotElevatedTasks") ?? "Fenêtre non élevée : la création et la lecture des tâches planifiées seront refusées par Windows."), alerte: true);
            }

            _ = RafraichirTacheArretAsync();
        }

        private async Task RafraichirTacheArretAsync()
        {
            try
            {
                _tacheArretExiste = await Task.Run(TaskSchedulerHelper.ShutdownTaskExists);
                MetAJourBoutonAutomatiqueArret();
            }
            catch { }
        }

        private void MetAJourBoutonAutomatiqueArret()
        {
            BoutonAutomatiqueArret.Content = _tacheArretExiste
                ? (LanguageManager.Get("Automatique ON")  ?? "Automatique ON")
                : (LanguageManager.Get("Automatique OFF") ?? "Automatique OFF");

            BoutonAutomatiqueArret.Style = (Style)FindResource(
                _tacheArretExiste ? "BoutonSucces" : "BoutonDanger");
        }

        private string ModeArretChoisi() =>
            ChoixTypeArret.SelectedIndex == 1 ? ModeVeille : ModeArret;

        private void ChargerConfigArret()
        {
            if (File.Exists(FichierConfigArret))
            {
                int heure = 1, minute = 30;
                string mode = ModeVeille;

                foreach (string ligne in File.ReadAllLines(FichierConfigArret))
                {
                    if (ligne.StartsWith("Hour=", StringComparison.Ordinal) && int.TryParse(ligne[5..], out int h))
                        heure = h;
                    else if (ligne.StartsWith("Minute=", StringComparison.Ordinal) && int.TryParse(ligne[7..], out int m))
                        minute = m;
                    else if (ligne.StartsWith("Mode=", StringComparison.Ordinal))
                        mode = ligne[5..].Trim();
                }

                ChampHeureArret.Text  = heure.ToString();
                ChampMinuteArret.Text = minute.ToString();
                ChoixTypeArret.SelectedIndex = mode.Equals(ModeVeille, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                return;
            }

            // Pas de fichier : on reprend l'heure ET le mode de la tâche planifiée,
            // si elle existe. Sans cela, une tâche restée en veille alors que le
            // fichier a disparu s'affichait comme un arrêt (comportement de la
            // version WinForms, perdu lors du portage).
            var horaire = TaskSchedulerHelper.GetShutdownTaskTime();
            string? modeTache = TaskSchedulerHelper.GetShutdownTaskMode();

            if (horaire != null)
            {
                ChampHeureArret.Text  = horaire.Value.hour.ToString();
                ChampMinuteArret.Text = horaire.Value.minute.ToString();
            }
            else
            {
                ChampHeureArret.Text  = "1";
                ChampMinuteArret.Text = "30";
            }

            ChoixTypeArret.SelectedIndex =
                (modeTache != null && modeTache.Equals(ModeVeille, StringComparison.OrdinalIgnoreCase))
                    ? 1
                    : (modeTache != null ? 0 : 1);
        }

        private void EcrireConfigArret(int heure, int minute, string mode)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FichierConfigArret)!);

            File.WriteAllLines(FichierConfigArret, new[]
            {
                $"Hour={heure}",
                $"Minute={minute}",
                $"Mode={mode}"
            });
        }

        private async void SurSauvegarderArret(object sender, RoutedEventArgs e)
        {
            try
            {
                int heure  = Borner(ChampHeureArret.Text,  0, 23, 1);
                int minute = Borner(ChampMinuteArret.Text, 0, 59, 30);
                string mode = ModeArretChoisi();

                EcrireConfigArret(heure, minute, mode);

                CoreLog.Info("On/Off", $"Réglage enregistré : {heure:00}:{minute:00}, "
                                     + $"mode {LibelleModeArret(mode)}");

                AfficherMessageOnOff(LanguageManager.Get("Configuration enregistrée.")
                                     ?? "Configuration enregistrée.");

                // Si la tâche existe déjà, on la recrée à la nouvelle heure.
                if (await Task.Run(TaskSchedulerHelper.ShutdownTaskExists))
                {
                    string resultat = await Task.Run(() =>
                        TaskSchedulerHelper.CreateShutdownTask(heure, minute, mode));

                    JournaliserTacheArret("mise à jour", resultat, heure, minute, mode);
                    AfficherMessageOnOff(resultat);
                }
                else
                {
                    CoreLog.Info("On/Off", "'Tâche MCEMonitor_Shutdown absente : rien à "
                                        + "reprogrammer (automatisation désactivée)");
                }

                await RafraichirTacheArretAsync();
            }
            catch (Exception ex)
            {
                AfficherMessageOnOff("Erreur : " + ex.Message, alerte: true);
            }
        }

        private async void SurAutomatisationArret(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_tacheArretExiste)
                {
                    var reponse = Boite.Show(this, 
                        LanguageManager.Get("Voulez-vous vraiment désactiver l'arrêt programmé ?")
                        ?? "Voulez-vous vraiment désactiver l'arrêt programmé ?",
                        LanguageManager.Get("Confirmation") ?? "Confirmation",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (reponse != MessageBoxResult.Yes)
                        return;

                    string resultat = await Task.Run(TaskSchedulerHelper.DeleteShutdownTask);

                    JournaliserTacheArret("suppression", resultat, 0, 0, null);
                    AfficherMessageOnOff(resultat);
                }
                else
                {
                    int heure  = Borner(ChampHeureArret.Text,  0, 23, 1);
                    int minute = Borner(ChampMinuteArret.Text, 0, 59, 30);
                    string mode = ModeArretChoisi();

                    string resultat = await Task.Run(() =>
                        TaskSchedulerHelper.CreateShutdownTask(heure, minute, mode));

                    JournaliserTacheArret("création", resultat, heure, minute, mode);
                    AfficherMessageOnOff(resultat);

                    EcrireConfigArret(heure, minute, mode);

                    CoreLog.Info("On/Off", $"Réglage enregistré : {heure:00}:{minute:00}, "
                                         + $"mode {LibelleModeArret(mode)}");
                }

                await RafraichirTacheArretAsync();
            }
            catch (Exception ex)
            {
                AfficherMessageOnOff("Erreur : " + ex.Message, alerte: true);
            }
        }

        private static string LibelleModeArret(string mode)
            => string.Equals(mode, ModeVeille, StringComparison.OrdinalIgnoreCase)
                ? "veille"
                : "arrêt";

        /// <summary>Échéance réelle de la tâche : cinq minutes avant l'heure demandée.</summary>
        private static (int heure, int minute) EcheanceTacheArret(int heure, int minute)
        {
            int m = minute - 5;
            int h = heure;

            if (m < 0)
            {
                m += 60;
                h -= 1;
                if (h < 0) h += 24;
            }

            return (h, m);
        }

        private static void JournaliserTacheArret(
            string action, string resultat, int heure, int minute, string? mode)
        {
            string details = string.Empty;

            if (mode is not null)
            {
                (int h, int m) = EcheanceTacheArret(heure, minute);
                details = $" (demandé {heure:00}:{minute:00} {LibelleModeArret(mode)}, "
                        + $"échéance {h:00}:{m:00})";
            }

            string message = $"Tâche MCEMonitor_Shutdown : {action}{details}"
                           + $" -> {resultat.Trim()}";

            if (resultat.Contains("ERREUR", StringComparison.OrdinalIgnoreCase))
                CoreLog.Warn("On/Off", message);
            else
                CoreLog.Info("On/Off", message);
        }

        private void AfficherMessageOnOff(string message, bool alerte = false)
        {
            MessageOnOff.Text = message;
            MessageOnOff.Foreground = (Brush)FindResource(alerte ? "Erreur" : "TexteSecondaire");
            MessageOnOff.Visibility = Visibility.Visible;
        }

        // ============================================================
        //  ONGLET NETMONITOR
        // ============================================================

        private readonly DispatcherTimer _minuteurServiceNet = new() { Interval = TimeSpan.FromSeconds(3) };

        private bool _basculeNetEnCours;
        private bool _tacheNetExiste;

        private static string CheminServiceNet => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "NetMonitor.Service.exe");

        private static string FichierConfigNet => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "NetMonitor.config");

        private static bool ServiceNetEnCours =>
            Process.GetProcessesByName("NetMonitor.Service").Length > 0;

        private static bool InterfaceNetOuverte =>
            Process.GetProcessesByName("NetMonitor.UI").Length > 0;

        private void ChargerNet()
        {
            MetAJourEtatServiceNet();
            MetAJourBoutonAutomatiqueNet();
            ChargerReglagesNet();

            if (!EstEleve())
            {
                AfficherMessageNet((LanguageManager.Get("Ui.NotElevatedService") ?? "Fenêtre non élevée : la gestion du service et des tâches planifiées sera refusée par Windows."), alerte: true);
            }

            _minuteurServiceNet.Tick += (s, e) => MetAJourEtatServiceNet();
            _minuteurServiceNet.Start();

            _ = RafraichirTacheNetAsync();
        }

        private void MetAJourEtatServiceNet()
        {
            if (_basculeNetEnCours)
                return;

            bool actif = ServiceNetEnCours;

            BasculeServiceNet.IsChecked = actif;

            TexteEtatServiceNet.Text = actif
                ? (LanguageManager.Get("Service NetMonitor : actif")  ?? "Service NetMonitor : actif")
                : (LanguageManager.Get("Service NetMonitor : arrêté") ?? "Service NetMonitor : arrêté");
        }

        private async Task RafraichirTacheNetAsync()
        {
            try
            {
                _tacheNetExiste = await Task.Run(TaskSchedulerHelper.NetMonitorTaskExists);
                MetAJourBoutonAutomatiqueNet();
            }
            catch { }
        }

        private void MetAJourBoutonAutomatiqueNet()
        {
            BoutonAutomatiqueNet.Content = _tacheNetExiste
                ? (LanguageManager.Get("Automatique ON")  ?? "Automatique ON")
                : (LanguageManager.Get("Automatique OFF") ?? "Automatique OFF");

            BoutonAutomatiqueNet.Style = (Style)FindResource(
                _tacheNetExiste ? "BoutonSucces" : "BoutonDanger");
        }

        private async void SurBasculeServiceNet(object sender, RoutedEventArgs e)
        {
            if (_basculeNetEnCours)
                return;

            _basculeNetEnCours = true;

            MessageNet.Visibility = Visibility.Collapsed;

            CoreLog.Info("NetMonitor", "Service : "
                                + (ServiceNetEnCours ? "arrêt demandé" : "démarrage demandé"));

            try
            {
                if (ServiceNetEnCours)
                {
                    if (InterfaceNetOuverte)
                    {
                        BasculeServiceNet.IsChecked = true;
                        AfficherMessageNet("Impossible d'arrêter NetMonitor.Service tant que NetMonitor.UI est ouvert.", alerte: true);
                        return;
                    }

                    foreach (var processus in Process.GetProcessesByName("NetMonitor.Service"))
                        processus.Kill();
                }
                else
                {
                    if (!File.Exists(CheminServiceNet))
                    {
                        BasculeServiceNet.IsChecked = false;
                        AfficherMessageNet("NetMonitor.Service.exe introuvable.", alerte: true);
                        return;
                    }

                    Process.Start(new ProcessStartInfo { FileName = CheminServiceNet, UseShellExecute = true });

                    await Task.Delay(1200);

                    if (!ServiceNetEnCours)
                    {
                        BasculeServiceNet.IsChecked = false;
                        AfficherMessageNet("Le service NetMonitor.Service n'a pas pu démarrer.", alerte: true);
                        return;
                    }

                    DemarrerTray("NetMonitor.Tray.exe");
                }
            }
            catch (Exception ex)
            {
                AfficherMessageNet("Erreur : " + ex.Message, alerte: true);
            }
            finally
            {
                _basculeNetEnCours = false;
            }

            await Task.Delay(700);
            MetAJourEtatServiceNet();

            CoreLog.Info("NetMonitor", "Service : "
                                + (ServiceNetEnCours ? "en cours" : "arrêté"));
        }

        private async void SurAutomatisationNet(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_tacheNetExiste)
                {
                    var reponse = Boite.Show(this, 
                        LanguageManager.Get("Voulez-vous vraiment désactiver l'automatisation NetMonitor ?")
                        ?? "Voulez-vous vraiment désactiver l'automatisation NetMonitor ?",
                        LanguageManager.Get("Confirmation") ?? "Confirmation",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (reponse != MessageBoxResult.Yes)
                        return;

                    string resultat = await Task.Run(TaskSchedulerHelper.DeleteNetMonitorTask);
                    // Parité WinForms : la tâche du Tray suit celle du service.
                    await Task.Run(TaskSchedulerHelper.DeleteNetTrayTask);
                    JournaliserTache("NetMonitor", "MCEMonitor_NetMonitorService", "suppression", resultat);
                    AfficherMessageNet(resultat);
                }
                else
                {
                    string resultat = await Task.Run(TaskSchedulerHelper.CreateNetMonitorTask);
                    // Parité WinForms : la tâche du Tray suit celle du service.
                    if (!TaskSchedulerHelper.NetTrayTaskExists())
                        await Task.Run(TaskSchedulerHelper.CreateNetTrayTask);
                    JournaliserTache("NetMonitor", "MCEMonitor_NetMonitorService", "création", resultat);
                    AfficherMessageNet(resultat);
                }

                await RafraichirTacheNetAsync();
            }
            catch (Exception ex)
            {
                AfficherMessageNet("Erreur : " + ex.Message, alerte: true);
            }
        }

        private void AfficherMessageNet(string message, bool alerte = false)
        {
            MessageNet.Text = message;
            MessageNet.Foreground = (Brush)FindResource(alerte ? "Erreur" : "TexteSecondaire");
            MessageNet.Visibility = Visibility.Visible;
        }

        private async void SurOuvrirNetUI(object sender, RoutedEventArgs e)
        {
            if (!ServiceNetEnCours)
            {
                var reponse = Boite.Show(this, 
                    (LanguageManager.Get("Le service NetMonitor n'est pas en cours d'exécution.")
                     ?? "Le service NetMonitor n'est pas en cours d'exécution.")
                    + "\n\n" +
                    (LanguageManager.Get("Voulez-vous le démarrer maintenant ?")
                     ?? "Voulez-vous le démarrer maintenant ?"),
                    LanguageManager.Get("Service non démarré") ?? "Service non démarré",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (reponse != MessageBoxResult.Yes)
                    return;

                if (!File.Exists(CheminServiceNet))
                {
                    AfficherMessageNet("NetMonitor.Service.exe introuvable.", alerte: true);
                    return;
                }

                Process.Start(new ProcessStartInfo { FileName = CheminServiceNet, UseShellExecute = true });

                await Task.Delay(1200);
                MetAJourEtatServiceNet();

                // Parité WinForms : le Tray démarre avec le service.
                DemarrerTray("NetMonitor.Tray.exe");
            }

            try
            {
                string cheminUI = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NetMonitor.UI.exe");

                if (!File.Exists(cheminUI))
                {
                    AfficherMessageNet("NetMonitor.UI.exe est introuvable dans le dossier de MCEMonitor.", alerte: true);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName  = cheminUI,
                    Arguments = $"--from-mcem -lang {LanguageManager.CurrentLanguage ?? "fr-FR"}",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AfficherMessageNet("Erreur lors de l'ouverture de NetMonitor.UI : " + ex.Message, alerte: true);
            }
        }

        // ============================================================
        //  Réglages de NetMonitor
        // ============================================================

        private void ChargerReglagesNet()
        {
            var config = LireConfig(FichierConfigNet);

            // Valeurs par défaut : celles du service (NetMonitorSettings), pour qu'une
            // clé absente du fichier n'affiche pas une coche contraire à ce que le
            // service applique réellement.
            CaseNetAutoBlock.IsChecked          = EstVrai(config, "AutoBlock", false);
            CaseNetAutoBlockCritical.IsChecked  = EstVrai(config, "AutoBlockCritical", true);
            CaseNetAutoBlockWarning.IsChecked   = EstVrai(config, "AutoBlockWarning", true);
            CaseNetBlockKnownScanners.IsChecked = EstVrai(config, "BlockKnownScanners", false);
            CaseNetBlockBots.IsChecked          = EstVrai(config, "BlockBots", false);
            CaseNetBlockCrawlers.IsChecked      = EstVrai(config, "BlockCrawlers", true);
        }

        private async void SurEnregistrerReglagesNet(object sender, RoutedEventArgs e)
        {
            try
            {
                // Seules les clés de blocage sont touchées. La fusion laisse intact
                // tout le reste du fichier : patterns de sévérité, réseaux ignorés,
                // listes officielles Microsoft et Google, threat intelligence, GeoIP,
                // date de mise à jour de la blacklist… Un File.WriteAllLines, ici,
                // les effaçait à chaque enregistrement.
                var modifications = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AutoBlock"]          = TexteBooleen(CaseNetAutoBlock.IsChecked),
                    ["AutoBlockCritical"]  = TexteBooleen(CaseNetAutoBlockCritical.IsChecked),
                    ["AutoBlockWarning"]   = TexteBooleen(CaseNetAutoBlockWarning.IsChecked),
                    ["BlockKnownScanners"] = TexteBooleen(CaseNetBlockKnownScanners.IsChecked),
                    ["BlockBots"]          = TexteBooleen(CaseNetBlockBots.IsChecked),
                    ["BlockCrawlers"]      = TexteBooleen(CaseNetBlockCrawlers.IsChecked)
                };

                FusionnerConfigNet(modifications);

                CoreLog.Info("NetMonitor", "Réglages enregistrés");

                bool recharge = await ServiceReload.RechargerAsync(ServiceReload.PipeNet);

                if (recharge)
                {
                    CoreLog.Info("NetMonitor", "Service rechargé à chaud");
                    AfficherMessageNet(LanguageManager.Get("Réglages enregistrés, service rechargé.")
                                       ?? "Réglages enregistrés, service rechargé.");
                }
                else if (!ServiceNetEnCours)
                {
                    CoreLog.Info("NetMonitor", "Service arrêté : réglages appliqués à son démarrage");
                    AfficherMessageNet(LanguageManager.Get("Réglages enregistrés. Le service n'est pas démarré : ils seront appliqués à son démarrage.")
                                       ?? "Réglages enregistrés. Le service n'est pas démarré : ils seront appliqués à son démarrage.");
                }
                else
                {
                    CoreLog.Warn("NetMonitor", "Service non notifié : redémarrage nécessaire");
                    AfficherMessageNet(LanguageManager.Get("Réglages enregistrés, mais le service n'a pas pu être notifié : redémarrez-le.")
                                       ?? "Réglages enregistrés, mais le service n'a pas pu être notifié : redémarrez-le.");
                }
            }
            catch (Exception ex)
            {
                AfficherMessageNet("Erreur lors de l'enregistrement : " + ex.Message, alerte: true);
            }
        }

        private static string TexteBooleen(bool? valeur) => valeur == true ? "true" : "false";

        /// <summary>
        /// Met à jour quelques clés de NetMonitor.config sans rien perdre du reste.
        ///
        /// Les valeurs sont remplacées sur place, les clés absentes sont ajoutées à
        /// la suite du bloc de blocage automatique, et tout ce qui n'est pas cité —
        /// commentaires, patterns, listes officielles, date de la blacklist — est
        /// recopié tel quel. Réécrire le fichier entier, comme le faisait
        /// l'enregistrement précédent, remettait le service sur ses valeurs par
        /// défaut sans le moindre avertissement.
        /// </summary>
        private void FusionnerConfigNet(Dictionary<string, string> modifications)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FichierConfigNet)!);

            var restants = new Dictionary<string, string>(modifications, StringComparer.OrdinalIgnoreCase);
            var lignes   = new List<string>();

            if (File.Exists(FichierConfigNet))
                lignes.AddRange(File.ReadAllLines(FichierConfigNet));

            int derniereModifiee = -1;

            for (int i = 0; i < lignes.Count; i++)
            {
                string ligne = lignes[i];

                if (string.IsNullOrWhiteSpace(ligne) || ligne.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    continue;

                var parties = ligne.Split('=', 2);
                if (parties.Length != 2)
                    continue;

                string cle = parties[0].Trim();

                if (restants.TryGetValue(cle, out var valeur))
                {
                    lignes[i] = cle + "=" + valeur;
                    restants.Remove(cle);
                    derniereModifiee = i;
                }
            }

            if (restants.Count > 0)
            {
                var manquantes = restants.Select(kv => kv.Key + "=" + kv.Value).ToList();

                if (derniereModifiee >= 0)
                {
                    lignes.InsertRange(derniereModifiee + 1, manquantes);
                }
                else
                {
                    if (lignes.Count > 0 && lignes[^1].Length > 0)
                        lignes.Add(string.Empty);

                    lignes.Add("# --- Blocage automatique ---");
                    lignes.AddRange(manquantes);
                }
            }

            File.WriteAllLines(FichierConfigNet, lignes);
        }

        // Lecture commune des fichiers de configuration « clé=valeur ».
        private static Dictionary<string, string> LireConfig(string chemin)
        {
            var valeurs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(chemin))
                return valeurs;

            foreach (string ligne in File.ReadAllLines(chemin))
            {
                if (string.IsNullOrWhiteSpace(ligne) || ligne.StartsWith("#", StringComparison.Ordinal))
                    continue;

                var parties = ligne.Split('=', 2);
                if (parties.Length != 2)
                    continue;

                valeurs[parties[0].Trim()] = parties[1].Trim();
            }

            return valeurs;
        }

        // ============================================================
        //  ONGLET À PROPOS
        // ============================================================

        private void ChargerAbout()
        {
            // Même source que la version WinForms : la version dite « display »,
            // c'est-à-dire l'InformationalVersion amputée du suffixe de build GitVersion.
            var assembly = Assembly.GetExecutingAssembly();

            string versionComplete = assembly
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .OfType<AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion ?? "Inconnue";

            // GitVersion produit « 2.3.11-7+Branch.main.Sha… » : on ne garde que le
            // numéro de version, sans la branche ni le numéro de commit.
            string versionAffichable = versionComplete.Split('+')[0].Split('-')[0];

            TexteVersion.Text = "Version " + versionAffichable;
            TexteLicence.Text = LicenceMit;
        }

        private static string DossierLogs => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "Logs");

        private void SurOuvrirLogs(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(DossierLogs);
                Process.Start("explorer.exe", DossierLogs);
            }
            catch (Exception ex)
            {
                Boite.Show(this, 
                    (LanguageManager.Get("Impossible d'ouvrir le dossier Logs.")
                     ?? "Impossible d'ouvrir le dossier Logs.") + "\n\n" + ex.Message,
                    LanguageManager.Get("Erreur") ?? "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SurPurgerLogs(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Directory.Exists(DossierLogs))
                {
                    foreach (string fichier in Directory.GetFiles(DossierLogs))
                        File.Delete(fichier);
                }
                else
                {
                    Directory.CreateDirectory(DossierLogs);
                }

                Boite.Show(this, 
                    LanguageManager.Get("Tous les logs ont été purgés.") ?? "Tous les logs ont été purgés.",
                    LanguageManager.Get("Succès") ?? "Succès",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Boite.Show(this, 
                    (LanguageManager.Get("Erreur lors de la purge des logs.")
                     ?? "Erreur lors de la purge des logs.") + "\n\n" + ex.Message,
                    LanguageManager.Get("Erreur") ?? "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // Le choix est enregistré, puis appliqué au démarrage suivant : les styles
        // référencent les couleurs en StaticResource, résolu une seule fois au
        // chargement.
        private void SurChoisirTheme(object sender, RoutedEventArgs e)
        {
            bool estClair = GestionnaireTheme.ThemeChoisi() == GestionnaireTheme.Clair;

            string etat = estClair
                ? (LanguageManager.Get("Le thème clair est actif.") ?? "Le thème clair est actif.")
                : (LanguageManager.Get("Le thème sombre est actif.") ?? "Le thème sombre est actif.");

            string question = estClair
                ? (LanguageManager.Get("Passer au thème sombre ?") ?? "Passer au thème sombre ?")
                : (LanguageManager.Get("Passer au thème clair ?") ?? "Passer au thème clair ?");

            var reponse = Boite.Show(this,
                etat + "\n\n" + question,
                LanguageManager.Get("Changer de thème") ?? "Changer de thème",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (reponse != MessageBoxResult.Yes)
                return;

            bool ok = GestionnaireTheme.Enregistrer(
                estClair ? GestionnaireTheme.Sombre : GestionnaireTheme.Clair);

            if (!ok)
            {
                Boite.Show(this,
                    LanguageManager.Get("Impossible d'enregistrer le choix du thème.")
                        ?? "Impossible d'enregistrer le choix du thème.",
                    LanguageManager.Get("Changer de thème") ?? "Changer de thème",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Le thème est lu au démarrage : on propose de relancer tout de suite, en
            // précisant que les interfaces déjà ouvertes garderont l'ancien thème.
            var redemarrer = Boite.Show(this,
                (LanguageManager.Get("Le thème sera appliqué au prochain démarrage de MCEMonitor.")
                    ?? "Le thème sera appliqué au prochain démarrage de MCEMonitor.")
                + "\n\n"
                + (LanguageManager.Get("Les interfaces de modules déjà ouvertes conserveront l'ancien thème jusqu'à leur fermeture.")
                    ?? "Les interfaces de modules déjà ouvertes conserveront l'ancien thème jusqu'à leur fermeture.")
                + "\n\n"
                + (LanguageManager.Get("Redémarrer MCEMonitor maintenant ?")
                    ?? "Redémarrer MCEMonitor maintenant ?"),
                LanguageManager.Get("Changer de thème") ?? "Changer de thème",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (redemarrer == MessageBoxResult.Yes)
                Redemarrer();
        }

        /// <summary>
        /// Relance l'application et ferme l'instance courante.
        /// </summary>
        private static void Redemarrer()
        {
            try
            {
                string? executable = Environment.ProcessPath;

                if (string.IsNullOrEmpty(executable))
                {
                    Boite.Show(null!,
                        LanguageManager.Get("Redémarrage impossible : exécutable introuvable.")
                            ?? "Redémarrage impossible : exécutable introuvable.",
                        LanguageManager.Get("Changer de thème") ?? "Changer de thème",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // Le mutex doit être rendu avant que le successeur ne démarre.
                App.LibererInstanceUnique();

                Process.Start(new ProcessStartInfo
                {
                    FileName        = executable,
                    UseShellExecute = true
                });

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Boite.Show(null!,
                    (LanguageManager.Get("Redémarrage impossible : ") ?? "Redémarrage impossible : ") + ex.Message,
                    LanguageManager.Get("Erreur") ?? "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private const string LicenceMit = """
            Permission is hereby granted, free of charge, to any person obtaining a copy
            of this software and associated documentation files (the "Software"), to deal
            in the Software without restriction, including without limitation the rights
            to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
            copies of the Software, and to permit persons to whom the Software is
            furnished to do so, subject to the following conditions:

            The above copyright notice and this permission notice shall be included in all
            copies or substantial portions of the Software.

            THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
            IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
            FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
            AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
            LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
            OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
            SOFTWARE.
            """;
    }
}
