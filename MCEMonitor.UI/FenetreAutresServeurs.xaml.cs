using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MCEMonitor.Languages;
using MCEMonitor.UI.Partage;

namespace MCEMonitor.UI
{
    /// <summary>
    /// Fenêtre des autres machines du réseau local.
    ///
    /// Une seule machine expose le 443 et porte le certificat : c'est celle qui
    /// exécute MCEMonitor. Les autres sont déclarées ici, et la passerelle relaie
    /// leurs modules sous des noms aplatis — netmonitor-mce-salon.mcemonitor.org —
    /// un wildcard ne couvrant qu'un seul niveau sous le domaine.
    ///
    /// La détection ne demande aucun identifiant : le nom vient de la résolution
    /// inverse, le type de module du realm renvoyé par son 401.
    /// </summary>
    public partial class FenetreAutresServeurs : Window
    {
        private static readonly string[] NomsModules =
        {
            "NetMonitor", "RomMonitor", "SystemMonitor", "MediaMonitor"
        };

        /// <summary>
        /// Ports interrogés à la détection. Chaque module occupe une plage réservée —
        /// 11000-11050 pour MediaMonitor, 12000-12050 pour RomMonitor, 13000-13050
        /// pour SystemMonitor, 14000-14050 pour NetMonitor — et les serveurs refusent
        /// désormais tout port hors de la leur. Les anciens ports par défaut sont
        /// conservés en fin de liste, pour les installations pas encore à jour.
        /// </summary>
        private static readonly int[] PortsConnus = Plages();

        private static int[] Plages()
        {
            // D abord les ports les plus probables : résultat quasi immédiat dans le
            // cas courant, sans attendre le balayage complet.
            var ports = new List<int>
            {
                11000, 12000, 13000, 14000,          // bas de chaque plage
                11001, 12001, 13001, 14001, 14003,   // ports en service chez Christophe
                8081, 8083, 8085, 8086               // anciens défauts, avant les plages
            };

            foreach (var (debut, fin) in new[]
                     {
                         (11000, 11050), (12000, 12050), (13000, 13050), (14000, 14050)
                     })
            {
                for (int port = debut; port <= fin; port++)
                    if (!ports.Contains(port))
                        ports.Add(port);
            }

            return ports.ToArray();
        }

        private readonly List<Bloc> _blocs = new();
        private readonly string _domaine;
        private readonly Dictionary<string, int> _portsDefaut;

        /// <summary>Résultat, à lire après un DialogResult vrai.</summary>
        public List<ServeurSupplementaire> Serveurs { get; private set; } = new();

        public FenetreAutresServeurs(HttpsConfig config)
        {
            InitializeComponent();

            _domaine = config.Domaine.Trim().TrimEnd('.').ToLowerInvariant();

            _portsDefaut = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["NetMonitor"]    = PortDe(config.CibleNetMonitor, 14001),
                ["RomMonitor"]    = PortDe(config.CibleRomMonitor, 12001),
                ["SystemMonitor"] = PortDe(config.CibleSystemMonitor, 13001),
                ["MediaMonitor"]  = PortDe(config.CibleMediaMonitor, 11001)
            };

            if (config.ServeursSupplementaires.Count > 0)
                foreach (var serveur in config.ServeursSupplementaires)
                    AjouterBloc(serveur);
            else
                AjouterBloc(null);

            // À l ouverture, on vérifie ce qui répond réellement : une machine arrêtée
            // ou un module déplacé se voit aussitôt, sans avoir à cliquer sur Détecter.
            // Les blocs vides sont ignorés, et la détection ne décoche rien — elle
            // ajoute seulement les marques de ce qui répond.
            Loaded += async (_, _) =>
            {
                var renseignes = _blocs.Where(b => b.Ip.Text.Trim().Length > 0).ToList();

                if (renseignes.Count > 0)
                    await Task.WhenAll(renseignes.Select(DetecterAsync));
            };
        }

        /// <summary>Le port d'une cible « hôte:port », ou le défaut du module.</summary>
        private static int PortDe(string cible, int defaut)
        {
            int deuxPoints = cible.LastIndexOf(':');
            if (deuxPoints < 0) return defaut;

            return int.TryParse(cible.Substring(deuxPoints + 1).Trim(), out int port) && port > 0 && port <= 65535
                ? port
                : defaut;
        }

        private void EnTete_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); } catch { }
        }

        private void SurFermer(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SurNouveau(object sender, RoutedEventArgs e) => AjouterBloc(null);

        // ============================================================
        //  Construction d'un bloc machine
        // ============================================================
        private void AjouterBloc(ServeurSupplementaire? modele)
        {
            var bloc = new Bloc();

            var racine = new Border
            {
                Background    = Couleur("Surface", Brushes.White),
                BorderBrush   = Couleur("Bordure", Brushes.LightGray),
                BorderThickness = new Thickness(1),
                CornerRadius  = new CornerRadius(6),
                Padding       = new Thickness(12, 10, 12, 12),
                Margin        = new Thickness(0, 0, 0, 12)
            };

            var pile = new StackPanel();

            // --- Adresse, détection, réactualisation ---
            var ligneIp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 6)
            };

            ligneIp.Children.Add(Etiquette(L("Adresse IP :"), 100));

            bloc.Ip = new TextBox
            {
                Width = 150,
                Style = StyleDe("ChampParametre"),
                Text  = modele?.Ip ?? ""
            };
            ligneIp.Children.Add(bloc.Ip);

            bloc.Detecter = new Button
            {
                Content = L("Détecter"),
                Style   = StyleDe("BoutonSecondaire"),
                Width   = 100,
                Height  = 26,
                Padding = new Thickness(0),
                Margin  = new Thickness(8, 0, 0, 0)
            };
            bloc.Detecter.Click += SurDetecter;
            ligneIp.Children.Add(bloc.Detecter);

            // Petit bouton de réactualisation : les services peuvent avoir été
            // démarrés après le premier essai, sans quitter la fenêtre.
            bloc.Actualiser = new Button
            {
                Content   = "↻",
                Style     = StyleDe("BoutonSecondaire"),
                Width     = 34,
                Height    = 26,
                Padding   = new Thickness(0),
                FontSize  = 14,
                Margin    = new Thickness(6, 0, 0, 0),
                ToolTip   = L("Réactualiser la détection")
            };
            bloc.Actualiser.Click += SurActualiser;
            ligneIp.Children.Add(bloc.Actualiser);

            bloc.Etat = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Couleur("TexteSecondaire", Brushes.Gray),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 380
            };
            ligneIp.Children.Add(bloc.Etat);

            pile.Children.Add(ligneIp);

            // --- Nom de la machine : déduit, mais corrigeable ---
            var ligneNom = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };

            ligneNom.Children.Add(Etiquette(L("Machine :"), 100));

            bloc.Machine = new TextBox
            {
                Width = 200,
                Style = StyleDe("ChampParametre"),
                Text  = modele?.Machine ?? ""
            };
            bloc.Machine.TextChanged += (_, _) => MettreAJourApercu(bloc);
            ligneNom.Children.Add(bloc.Machine);

            ligneNom.Children.Add(new TextBlock
            {
                Text = L("donne le nom des adresses publiées"),
                FontSize = 11,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Couleur("TexteSecondaire", Brushes.Gray)
            });

            pile.Children.Add(ligneNom);

            // En-tête des colonnes : sans lui, la case à cocher ne disait pas qu'elle
            // publiait le sous-domaine.
            var entete = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            entete.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            entete.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            entete.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            entete.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            foreach (var (texte, colonne, span) in new[]
                     {
                         (L("À publier"), 0, 2), (L("Port"), 2, 1), (L("Adresse publiée"), 3, 1)
                     })
            {
                var titre = new TextBlock
                {
                    Text = texte,
                    FontSize = 10,
                    Foreground = Couleur("TexteSecondaire", Brushes.Gray)
                };

                Grid.SetColumn(titre, colonne);
                Grid.SetColumnSpan(titre, span);

                entete.Children.Add(titre);
            }

            pile.Children.Add(entete);

            // --- Un module par ligne : coché = publié ---
            foreach (string module in NomsModules)
            {
                var ligne = new Grid { Margin = new Thickness(0, 0, 0, 4) };

                ligne.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                ligne.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                ligne.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                ligne.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var caseModule = new CheckBox
                {
                    Content = module,
                    Foreground = Couleur("Texte", Brushes.Black),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(caseModule, 0);
                ligne.Children.Add(caseModule);

                // Marque de détection : verte quand le module a répondu. Sans elle,
                // on ne savait pas ce que la détection avait réellement trouvé.
                var marque = new TextBlock
                {
                    Text = "",
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Couleur("Succes", Brushes.Green)
                };
                Grid.SetColumn(marque, 1);
                ligne.Children.Add(marque);

                var port = new TextBox
                {
                    Width = 70,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Style = StyleDe("ChampParametre"),
                    Text = _portsDefaut.TryGetValue(module, out int p) ? p.ToString() : ""
                };
                Grid.SetColumn(port, 2);
                ligne.Children.Add(port);

                var apercu = new TextBlock
                {
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Couleur("TexteSecondaire", Brushes.Gray),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(apercu, 3);
                ligne.Children.Add(apercu);

                bloc.Lignes[module] = new Ligne
                {
                    Case = caseModule,
                    Marque = marque,
                    Port = port,
                    Apercu = apercu
                };

                caseModule.Checked   += (_, _) => MettreAJourApercu(bloc);
                caseModule.Unchecked += (_, _) => MettreAJourApercu(bloc);
                port.TextChanged     += (_, _) => MettreAJourApercu(bloc);

                pile.Children.Add(ligne);
            }

            racine.Child = pile;
            bloc.Conteneur = racine;

            // Pré-remplissage depuis un serveur déjà enregistré.
            if (modele != null)
            {
                foreach (var module in modele.Modules)
                {
                    if (!bloc.Lignes.TryGetValue(module.Module, out var ligne)) continue;

                    ligne.Case.IsChecked = true;
                    ligne.Port.Text = module.Port.ToString();
                }
            }

            _blocs.Add(bloc);
            ZoneServeurs.Children.Add(racine);

            MettreAJourApercu(bloc);
        }

        private TextBlock Etiquette(string texte, double largeur) => new()
        {
            Text = texte,
            Width = largeur,
            Style = StyleDe("Libelle"),
            VerticalAlignment = VerticalAlignment.Center
        };

        /// <summary>Aperçu des adresses publiées par ce bloc, recalculé à chaque saisie.</summary>
        private void MettreAJourApercu(Bloc bloc)
        {
            string machine = GenerateurCaddyfile.Slug(bloc.Machine.Text);

            foreach (var (module, ligne) in bloc.Lignes)
            {
                bool publie = ligne.Case.IsChecked == true;

                ligne.Apercu.Text = !publie
                    ? ""
                    : machine.Length == 0
                        ? L("nom de machine attendu")
                        : Hote(module, machine);
            }
        }

        private string Hote(string module, string machine)
        {
            string prefixe = GenerateurCaddyfile.Slug(module);
            string nom = $"{prefixe}-{machine}";

            return _domaine.Length == 0 ? nom : $"{nom}.{_domaine}";
        }

        // ============================================================
        //  Détection : le nom par résolution inverse, les modules par leur realm
        // ============================================================
        private async void SurDetecter(object sender, RoutedEventArgs e)
        {
            var bloc = _blocs.FirstOrDefault(b => b.Detecter == sender);
            if (bloc != null) await DetecterAsync(bloc);
        }

        private async void SurActualiser(object sender, RoutedEventArgs e)
        {
            var bloc = _blocs.FirstOrDefault(b => b.Actualiser == sender);
            if (bloc != null) await DetecterAsync(bloc);
        }

        private async Task DetecterAsync(Bloc bloc)
        {
            TexteErreur.Text = "";

            string ip = bloc.Ip.Text.Trim();

            if (ip.Length == 0)
            {
                bloc.Etat.Text = L("Saisissez d’abord l’adresse IP.");
                return;
            }

            bloc.Detecter.IsEnabled  = false;
            bloc.Actualiser.IsEnabled = false;
            bloc.Etat.Foreground = Couleur("TexteSecondaire", Brushes.Gray);
            bloc.Etat.Text = L("Recherche…");

            try
            {
                // 1. Le nom : le routeur connaît ses machines.
                string? nom = await ResoudreNomAsync(ip);

                if (!string.IsNullOrEmpty(nom))
                    bloc.Machine.Text = nom;

                // 2. Les modules : chacun annonce son nom dans l'en-tête de son 401.
                var trouves = await ChercherModulesAsync(ip);

                // Les marques repartent de zéro à chaque passage : une machine dont
                // on vient d'arrêter un service ne doit pas garder sa coche verte.
                foreach (var ligne in bloc.Lignes.Values)
                    ligne.Marque.Text = "";

                foreach (var (module, port) in trouves)
                {
                    if (!bloc.Lignes.TryGetValue(module, out var ligne))
                    {
                        // Un service inconnu répond : on le signale plutôt que de
                        // l'ignorer en silence, sinon la détection semble incomplète.
                        CoreLog.Warn("HTTPS", $"Détection {ip} : service inconnu « {module} » sur {port}.");
                        continue;
                    }

                    ligne.Marque.Text = "✔";
                    ligne.Case.IsChecked = true;
                    ligne.Port.Text = port.ToString();
                }

                string detail = string.Join(", ", trouves.Select(t => $"{t.Module} ({t.Port})"));

                bloc.Etat.Foreground = Couleur(trouves.Count > 0 ? "Succes" : "Erreur", Brushes.Gray);
                bloc.Etat.Text = trouves.Count == 0
                    ? L("Aucun module ne répond.")
                    : string.Format(L("{0} module(s) trouvé(s) : {1}."), trouves.Count, detail);

                if (nom == null && trouves.Count > 0)
                    bloc.Etat.Text += " " + L("Nom introuvable : saisissez-le.");

                // Sans trace, impossible de comprendre ce que la détection a vu.
                CoreLog.Info("HTTPS", trouves.Count == 0
                    ? $"Détection {ip} : aucun module ne répond."
                    : $"Détection {ip} : {detail}.");

                MettreAJourApercu(bloc);
            }
            catch (Exception ex)
            {
                bloc.Etat.Foreground = Couleur("Erreur", Brushes.Red);
                bloc.Etat.Text = ex.Message;
            }
            finally
            {
                bloc.Detecter.IsEnabled   = true;
                bloc.Actualiser.IsEnabled = true;
            }
        }

        /// <summary>
        /// Résolution inverse : « 192.168.1.19 » donne « serveur-mce.home », dont on
        /// ne garde que le premier segment. Renvoie null si le réseau ne sait pas.
        /// </summary>
        private static async Task<string?> ResoudreNomAsync(string ip)
        {
            try
            {
                var entree = await System.Net.Dns.GetHostEntryAsync(ip);

                string nom = entree.HostName ?? "";
                int point = nom.IndexOf('.');
                if (point > 0) nom = nom.Substring(0, point);

                string propre = GenerateurCaddyfile.Slug(nom);
                return propre.Length == 0 ? null : propre;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Interroge les ports des quatre plages, sans identifiant. Un module protégé
        /// répond « 401 » avec son nom : c'est ce realm qui l'identifie, pas le port.
        /// </summary>
        private static async Task<List<(string Module, int Port)>> ChercherModulesAsync(string ip)
        {
            var trouves = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // 600 ms suffisent sur un réseau local : une réponse arrive en quelques
            // millisecondes. Le délai n'est là que pour les ports filtrés, que le
            // pare-feu de Windows laisse tomber au lieu de les rejeter.
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(600) };

            using var limiteur = new SemaphoreSlim(48);

            bool complet = false;

            var essais = PortsConnus.Select(async port =>
            {
                if (complet) return;

                await limiteur.WaitAsync();

                try
                {
                    if (complet) return;

                    using var reponse = await client.GetAsync($"http://{ip}:{port}/api/summary");

                    if (reponse.StatusCode != System.Net.HttpStatusCode.Unauthorized)
                        return;

                    string realm = "";

                    // .NET range tout le paramètre dans Parameter, préfixe compris :
                    // la valeur brute est « realm="NetMonitor" ». La retirer par Trim
                    // donnait « realm="NetMonitor », qui ne correspondait à aucun module.
                    foreach (var entete in reponse.Headers.WwwAuthenticate)
                    {
                        string parametre = entete.Parameter ?? "";

                        int marque = parametre.IndexOf("realm=", StringComparison.OrdinalIgnoreCase);
                        if (marque < 0) continue;

                        realm = parametre
                            .Substring(marque + "realm=".Length)
                            .Trim()
                            .Trim('"')
                            .Trim();
                    }

                    if (realm.Length == 0) return;

                    lock (trouves)
                    {
                        // Premier port trouvé pour ce module : les suivants sont
                        // d'autres installations, on garde le plus courant.
                        if (!trouves.ContainsKey(realm))
                            trouves[realm] = port;

                        // Les quatre modules répondent : inutile de finir le balayage.
                        if (trouves.Count >= NomsModules.Length)
                            complet = true;
                    }
                }
                catch
                {
                    // Machine muette sur ce port : ce n'est pas une erreur, juste absent.
                }
                finally
                {
                    limiteur.Release();
                }
            });

            await Task.WhenAll(essais);

            return trouves.Select(kv => (kv.Key, kv.Value)).ToList();
        }

        // ============================================================
        //  Validation
        // ============================================================
        private void SurValider(object sender, RoutedEventArgs e)
        {
            TexteErreur.Text = "";

            CoreLog.Info("HTTPS", $"Autres serveurs : validation de {_blocs.Count} bloc(s).");

            var liste = new List<ServeurSupplementaire>();

            foreach (var bloc in _blocs)
            {
                string ip = bloc.Ip.Text.Trim();
                string machine = GenerateurCaddyfile.Slug(bloc.Machine.Text);

                var modules = new List<ModuleDistant>();

                foreach (var (nom, ligne) in bloc.Lignes)
                {
                    if (ligne.Case.IsChecked != true) continue;

                    if (!int.TryParse(ligne.Port.Text.Trim(), out int port) || port <= 0 || port > 65535)
                    {
                        TexteErreur.Text = string.Format(
                            L("Le port de {0} n’est pas valide."), nom);

                        CoreLog.Warn("HTTPS", $"Autres serveurs : port invalide pour {nom} (\"{ligne.Port.Text}\").");
                        return;
                    }

                    modules.Add(new ModuleDistant { Module = nom, Port = port });
                }

                // Bloc entièrement vide : simplement ignoré.
                if (ip.Length == 0 && modules.Count == 0) continue;

                if (ip.Length == 0)
                {
                    TexteErreur.Text = L("Une machine sans adresse IP ne peut pas être publiée.");
                    return;
                }

                if (machine.Length == 0)
                {
                    TexteErreur.Text = L("Le nom de la machine est vide : lancez la détection, ou saisissez-le.");
                    return;
                }

                if (modules.Count == 0)
                {
                    TexteErreur.Text = string.Format(L("Aucun module n’est coché pour {0}."), machine);
                    CoreLog.Warn("HTTPS", $"Autres serveurs : aucun module coché pour {machine}.");
                    return;
                }

                liste.Add(new ServeurSupplementaire
                {
                    Machine = machine,
                    Ip      = ip,
                    Modules = modules
                });
            }

            var doublon = liste
                .GroupBy(s => s.Machine, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);

            if (doublon != null)
            {
                TexteErreur.Text = string.Format(L("Deux machines portent le nom « {0} »."), doublon.Key);
                return;
            }

            Serveurs = liste;

            CoreLog.Info("HTTPS", $"Autres serveurs : {liste.Count} machine(s) validée(s) dans la fenêtre.");

            DialogResult = true;
            Close();
        }

        // ============================================================
        //  Petits services
        // ============================================================
        private static string L(string texte) => LanguageManager.Get(texte) ?? texte;

        private Brush Couleur(string cle, Brush defaut) => TryFindResource(cle) as Brush ?? defaut;

        private Style? StyleDe(string cle) => TryFindResource(cle) as Style;

        private sealed class Bloc
        {
            public Border Conteneur = null!;
            public TextBox Ip = null!;
            public TextBox Machine = null!;
            public TextBlock Etat = null!;
            public Button Detecter = null!;
            public Button Actualiser = null!;
            public Dictionary<string, Ligne> Lignes { get; } = new();
        }

        private sealed class Ligne
        {
            public CheckBox Case = null!;
            public TextBlock Marque = null!;
            public TextBox Port = null!;
            public TextBlock Apercu = null!;
        }
    }
}
