using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfSharpCore;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using NetMonitor.Service.Models;

namespace NetMonitor.Service
{
    /// <summary>Sections exportables du rapport.</summary>
    public enum PdfSection
    {
        Stats,
        Map,
        Blocked,
        Retries,
        History,
        Audit,
        Whitelist
    }

    /// <summary>
    /// Génère un rapport PDF composé d'un tableau par section demandée.
    ///
    /// Les colonnes sont placées à des abscisses fixes : un alignement par espaces
    /// ne tient pas avec une police proportionnelle, ce qui rendait l'ancien rapport
    /// illisible. Format A4 paysage — 786 points utiles —, avec les colonnes
    /// essentielles de l'interface : celles qui défilent à l'écran ou qui répètent
    /// la même information n'ont pas leur place sur une page imprimée.
    /// </summary>
    public static class PdfExporter
    {
        private const double MargeGauche = 28;
        private const double MargeHaut   = 34;
        private const double MargeBas    = 34;
        private const double HauteurLigne = 13;

        private static readonly XColor FondEntete  = XColor.FromArgb(0xE8, 0xE8, 0xE8);
        private static readonly XColor TraitTableau = XColor.FromArgb(0xBB, 0xBB, 0xBB);
        private static readonly XColor TraitLigne   = XColor.FromArgb(0xD8, 0xD8, 0xD8);

        private static readonly XFont PoliceTitre     = new("Arial", 17, XFontStyle.Bold);
        private static readonly XFont PoliceSoustitre = new("Arial", 9, XFontStyle.Regular);
        private static readonly XFont PoliceSection   = new("Arial", 12, XFontStyle.Bold);
        private static readonly XFont PoliceEntete    = new("Arial", 8, XFontStyle.Bold);
        private static readonly XFont PoliceTexte     = new("Arial", 8, XFontStyle.Regular);
        private static readonly XFont PolicePied      = new("Arial", 7, XFontStyle.Regular);
        private static readonly XFont PoliceChiffre   = new("Arial", 13, XFontStyle.Bold);

        private sealed class Colonne
        {
            public string Titre = "";
            public double Largeur;
        }

        private sealed class Tableau
        {
            public string Titre = "";
            public Colonne[] Colonnes = Array.Empty<Colonne>();
            public List<string[]> Lignes = new();
            public List<XColor?> Couleurs = new();
        }

        public static byte[] GenerateReport(
            string machineName,
            List<IntrusionEvent> history,
            List<BlockedIpInfo> blocked,
            List<IntrusionEvent> audit,
            List<BlockedIpRetryInfo> retries,
            List<ProcessTrustInfo> whitelist,
            GlobalStats? stats,
            IEnumerable<PdfSection> sections)
        {
            using var document = new PdfDocument();
            document.Info.Title = "Rapport NetMonitor";
            document.Info.Author = "MCEMonitor";

            var tableaux = new List<Tableau>();

            foreach (var section in sections.Distinct())
            {
                switch (section)
                {
                    // Ni les statistiques ni la carte ne sont des tableaux : ils
                    // occupent leurs propres pages, dessinées autour de l'en-tête.
                    case PdfSection.Stats:     break;
                    case PdfSection.Map:       break;
                    case PdfSection.Blocked:   tableaux.Add(TableauIpBloquees(blocked));           break;
                    case PdfSection.Retries:   tableaux.Add(TableauInterceptions(retries, blocked)); break;
                    case PdfSection.History:   tableaux.Add(TableauHistorique(history));            break;
                    case PdfSection.Audit:     tableaux.Add(TableauAudit(audit));                   break;
                    case PdfSection.Whitelist: tableaux.Add(TableauListeBlanche(whitelist));        break;
                }
            }

            double largeur = 842 - 2 * MargeGauche;   // A4 paysage

            // Un XGraphics par page, conservé jusqu'à la fin : PdfSharpCore n'en
            // autorise qu'un seul, et le pied de page se dessine dans le même.
            var graphiques = new List<XGraphics>();

            var page = NouvellePage(document);
            var gfx  = XGraphics.FromPdfPage(page);
            graphiques.Add(gfx);
            double y = MargeHaut;

            gfx.DrawString("Rapport NetMonitor", PoliceTitre, XBrushes.Black, new XPoint(MargeGauche, y));
            y += 20;
            gfx.DrawString($"Machine : {machineName}", PoliceSoustitre, XBrushes.DimGray, new XPoint(MargeGauche, y));
            gfx.DrawString($"Généré le {DateTime.Now:dd/MM/yyyy à HH:mm}", PoliceSoustitre, XBrushes.DimGray,
                           new XPoint(MargeGauche + 240, y));
            y += 24;

            // Les graphiques tiennent sous l'en-tête : on les y dessine directement,
            // sinon la première page ne contiendrait que le titre du rapport.
            // La position finale est reprise : sans elle, les tableaux se dessineraient
            // par-dessus les graphiques.
            if (sections.Contains(PdfSection.Stats) && stats != null)
                y = DessinerStatistiques(gfx, stats, largeur, y) + 18;

            // La carte prend une page entière : à 786 points de large, elle occupe
            // plus de la moitié de la hauteur utile.
            if (sections.Contains(PdfSection.Map))
            {
                byte[]? carte = ChargerCarte();

                if (carte != null)
                {
                    // La carte s'ajoute sous l'en-tête quand la page est encore
                    // vierge : sans cela, un rapport ne demandant que la carte
                    // produisait une première page réduite au titre.
                    bool pageVierge = !(sections.Contains(PdfSection.Stats) && stats != null);

                    if (!pageVierge)
                    {
                        page = NouvellePage(document);
                        gfx  = XGraphics.FromPdfPage(page);
                        graphiques.Add(gfx);
                        y = MargeHaut;
                    }

                    DessinerCarte(gfx, carte, page, largeur, y);
                    y = page.Height;   // page consommée : les tableaux suivants
                                       // ouvriront la leur
                }
            }

            foreach (var tableau in tableaux)
            {
                if (y > page.Height - MargeBas - 90)
                {
                    page = NouvellePage(document);
                    gfx  = XGraphics.FromPdfPage(page);
                    graphiques.Add(gfx);
                    y = MargeHaut;
                }

                y = DessinerTableau(ref gfx, ref page, tableau, largeur, y, document, graphiques);
            }

            DessinerPieds(document, machineName, graphiques);

            foreach (var graphique in graphiques)
                graphique.Dispose();

            using var ms = new MemoryStream();
            document.Save(ms, false);
            return ms.ToArray();
        }

        /// <summary>
        /// Chemin de la carte déposée par l'interface avant l'export.
        /// Le service lit un seul bloc de 4096 octets par commande IPC : une image
        /// ne peut pas y transiter, elle passe donc par un fichier.
        /// </summary>
        private static string CheminCarte => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor", "NetMonitor", "pdf_map.png");

        /// <summary>
        /// Lit la carte laissée par l'interface, puis l'efface : une carte oubliée
        /// ne doit jamais resservir pour un rapport ultérieur.
        /// </summary>
        private static byte[]? ChargerCarte()
        {
            try
            {
                string chemin = CheminCarte;
                if (!File.Exists(chemin)) return null;

                byte[] octets = File.ReadAllBytes(chemin);
                File.Delete(chemin);

                // Signature PNG : un fichier tronqué ou d'un autre type ferait
                // échouer XImage au milieu du rapport.
                if (octets.Length < 1000) return null;
                if (octets[0] != 0x89 || octets[1] != 0x50 || octets[2] != 0x4E || octets[3] != 0x47) return null;

                return octets;
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"[PDF] Carte illisible : {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Page de la carte : titre, image à la largeur utile, légende de l'échelle.
        /// </summary>
        private static void DessinerCarte(XGraphics gfx, byte[] png, PdfPage page, double largeur, double haut)
        {
            double y = haut;

            gfx.DrawString("Carte des pays d'origine", PoliceSection, XBrushes.Black,
                           new XPoint(MargeGauche, y));
            y += 20;

            gfx.DrawString("Toutes les alertes — le nombre croît du jaune au rouge",
                           PoliceSoustitre, XBrushes.DimGray, new XPoint(MargeGauche, y));
            y += 16;

            using var image = XImage.FromStream(() => new MemoryStream(png));

            if (image.PixelWidth <= 0 || image.PixelHeight <= 0) return;

            double ratio = image.PixelHeight / (double)image.PixelWidth;
            double largeurImage = largeur;
            double hauteurImage = largeurImage * ratio;

            // La carte ne doit pas empiéter sur le pied de page.
            double hauteurDisponible = page.Height - y - MargeBas - 16;

            if (hauteurImage > hauteurDisponible)
            {
                hauteurImage = hauteurDisponible;
                largeurImage = hauteurImage / ratio;
            }

            gfx.DrawImage(image, MargeGauche, y, largeurImage, hauteurImage);
        }

        private static PdfPage NouvellePage(PdfDocument document)
        {
            var page = document.AddPage();
            page.Size = PageSize.A4;
            page.Orientation = PageOrientation.Landscape;
            return page;
        }

        private static double DessinerTableau(ref XGraphics gfx, ref PdfPage page, Tableau tableau,
                                              double largeur, double y, PdfDocument document,
                                              List<XGraphics> graphiques)
        {
            gfx.DrawString(tableau.Titre, PoliceSection, XBrushes.Black, new XPoint(MargeGauche, y));
            y += 16;

            // Le cadre suit la portion du tableau dessinée sur la page courante :
            // il est fermé avant chaque changement de page, puis rouvert.
            var cadre = new XPen(TraitTableau, 0.8);
            double yHaut = y - 9;

            // Les pastilles de severite doivent s aligner verticalement : on reserve
            // la largeur du plus long libelle du tableau, puis on centre ce bloc
            // constant. Sans cela, « Safe » et « Critical » decalent le point.
            double largeurSeverite = 0;

            if (tableau.Colonnes.Length > 2)
            {
                foreach (var l in tableau.Lignes)
                {
                    if (l.Length <= 2) continue;
                    double w = gfx.MeasureString(l[2], PoliceTexte).Width;
                    if (w > largeurSeverite) largeurSeverite = w;
                }
            }

            y = DessinerEntete(gfx, tableau, largeur, y);

            foreach (var (ligne, couleur) in tableau.Lignes.Zip(tableau.Couleurs, (l, c) => (l, c)))
            {
                if (y > page.Height - MargeBas - HauteurLigne)
                {
                    gfx.DrawRectangle(cadre, MargeGauche, yHaut, largeur, (y - 9) - yHaut);

                    page = NouvellePage(document);
                    gfx  = XGraphics.FromPdfPage(page);
                    graphiques.Add(gfx);
                    y = MargeHaut;

                    gfx.DrawString(tableau.Titre + " (suite)", PoliceSection, XBrushes.DimGray, new XPoint(MargeGauche, y));
                    y += 16;

                    yHaut = y - 9;
                    y = DessinerEntete(gfx, tableau, largeur, y);
                }

                double x = MargeGauche;

                for (int i = 0; i < tableau.Colonnes.Length; i++)
                {
                    string texte = i < ligne.Length ? ligne[i] : "";

                    bool pastille = i == 2 && couleur != null;   // colonne « Sévérité »
                    bool centre   = i == 0 || i == 2;            // « Sens » et « Sévérité »

                    string affiche = Tronquer(gfx, texte, PoliceTexte,
                                              tableau.Colonnes[i].Largeur - 6 - (pastille ? 9 : 0));

                    double largeurTexte = gfx.MeasureString(affiche, PoliceTexte).Width;

                    // Pour la severite, la largeur retenue est celle du plus long
                    // libelle : les pastilles tombent ainsi toutes a la meme abscisse.
                    double largeurGroupe = pastille ? 9 + largeurSeverite : largeurTexte;

                    // Ces deux colonnes ne portent qu'un glyphe ou un mot court : les
                    // centrer dans leur case évite un alignement à gauche qui les fait
                    // flotter loin de leur en-tête.
                    double xCellule = centre
                        ? x + Math.Max(3, (tableau.Colonnes[i].Largeur - largeurGroupe) / 2)
                        : x;

                    if (pastille)
                        gfx.DrawEllipse(new XSolidBrush(couleur!.Value), xCellule, y - 5, 5, 5);

                    gfx.DrawString(affiche, PoliceTexte, XBrushes.Black,
                                   new XPoint(xCellule + (pastille ? 9 : 0), y));

                    x += tableau.Colonnes[i].Largeur;
                }

                // Quadrillage léger : sans lui, les lignes se lisent mal sur du texte
                // continu. Trait horizontal sous chaque ligne, traits verticaux aux
                // séparations de colonnes.
                var pen = new XPen(TraitLigne);

                gfx.DrawLine(pen, MargeGauche, y + 4, MargeGauche + largeur, y + 4);

                double xSep = MargeGauche;
                for (int i = 0; i < tableau.Colonnes.Length - 1; i++)
                {
                    xSep += tableau.Colonnes[i].Largeur;
                    gfx.DrawLine(pen, xSep, y - 9, xSep, y + 4);
                }

                y += HauteurLigne;
            }

            gfx.DrawRectangle(cadre, MargeGauche, yHaut, largeur, (y - 9) - yHaut);

            y += 18;
            return y;
        }

        private static double DessinerEntete(XGraphics gfx, Tableau tableau, double largeur, double y)
        {
            gfx.DrawRectangle(new XSolidBrush(FondEntete), MargeGauche, y - 9, largeur, HauteurLigne + 3);

            double x = MargeGauche;

            for (int i = 0; i < tableau.Colonnes.Length; i++)
            {
                string titre = tableau.Colonnes[i].Titre;

                // Les colonnes « Sens » et « Sévérité » ont leur contenu centré :
                // leur en-tête suit, sinon il flotte au-dessus du vide.
                double xTitre = (i == 0 || i == 2)
                    ? x + Math.Max(2, (tableau.Colonnes[i].Largeur - gfx.MeasureString(titre, PoliceEntete).Width) / 2)
                    : x;

                gfx.DrawString(titre, PoliceEntete, XBrushes.Black, new XPoint(xTitre, y));
                x += tableau.Colonnes[i].Largeur;
            }

            y += HauteurLigne;
            gfx.DrawLine(new XPen(TraitTableau), MargeGauche, y - 9, MargeGauche + largeur, y - 9);

            return y;
        }

        // ============================================================
        //  Construction des tableaux
        // ============================================================

        private static Tableau TableauIpBloquees(List<BlockedIpInfo> blocked)
        {
            var t = new Tableau
            {
                Titre = $"IP et processus bloqués au pare-feu ({blocked.Count})",
                Colonnes = new[]
                {
                    new Colonne { Titre = "Sens",           Largeur = 34 },
                    new Colonne { Titre = "Date",           Largeur = 58 },
                    new Colonne { Titre = "Sévérité",       Largeur = 62 },
                    new Colonne { Titre = "Type",           Largeur = 46 },
                    new Colonne { Titre = "IP / Processus", Largeur = 92 },
                    new Colonne { Titre = "Pays",           Largeur = 82 },
                    new Colonne { Titre = "FAI (ASN)",      Largeur = 72 },
                    new Colonne { Titre = "Organisation",   Largeur = 78 },
                    new Colonne { Titre = "Origine",        Largeur = 54 },
                    new Colonne { Titre = "Détecteur",      Largeur = 58 },
                    new Colonne { Titre = "Raison",         Largeur = 150 }
                }
            };

            foreach (var b in blocked.OrderByDescending(x => x.BlockedAt))
            {
                t.Lignes.Add(new[]
                {
                    b.IsOutbound ? "→" : "←",
                    b.BlockedAt.ToLocalTime().ToString("dd/MM HH:mm"),
                    b.Severity,
                    b.IsProcess ? "Processus" : "IP",
                    b.IsProcess ? (b.ProcessPath ?? b.Ip) : b.Ip,
                    string.IsNullOrWhiteSpace(b.Country) ? "-" : b.Country!,
                    string.IsNullOrWhiteSpace(b.Isp) ? "-" : b.Isp!,
                    string.IsNullOrWhiteSpace(b.Org) ? "-" : b.Org!,
                    b.Origin.ToString(),
                    b.Source,
                    b.Message
                });
                t.Couleurs.Add(CouleurSeverite(b.Severity));
            }

            return t;
        }

        private static Tableau TableauInterceptions(List<BlockedIpRetryInfo> retries, List<BlockedIpInfo> blocked)
        {
            var t = new Tableau
            {
                Titre = $"Tentatives sur IP bloquée ({retries.Count})",
                Colonnes = new[]
                {
                    new Colonne { Titre = "Sens",              Largeur = 34 },
                    new Colonne { Titre = "IP",                Largeur = 78 },
                    new Colonne { Titre = "Sévérité",          Largeur = 62 },
                    new Colonne { Titre = "Détecteur initial", Largeur = 70 },
                    new Colonne { Titre = "Pays",              Largeur = 80 },
                    new Colonne { Titre = "FAI (ASN)",         Largeur = 70 },
                    new Colonne { Titre = "Organisation",      Largeur = 76 },
                    new Colonne { Titre = "Reverse DNS",       Largeur = 76 },
                    new Colonne { Titre = "Bloquée le",        Largeur = 58 },
                    new Colonne { Titre = "Origine",           Largeur = 52 },
                    new Colonne { Titre = "Nb/T",              Largeur = 34 },
                    new Colonne { Titre = "Dernière",          Largeur = 72 }
                }
            };

            // Le pays, le FAI et l'organisation ne sont pas portés par la tentative :
            // ils viennent des IP bloquées, jointes par adresse.
            var infos = blocked
                .GroupBy(b => b.Ip, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key,
                              g => g.OrderByDescending(b => b.BlockedAt).First(),
                              StringComparer.OrdinalIgnoreCase);

            foreach (var r in retries.OrderByDescending(x => x.LastRetryAt))
            {
                infos.TryGetValue(r.Ip, out var info);

                t.Lignes.Add(new[]
                {
                    (info?.IsOutbound ?? false) ? "→" : "←",
                    r.Ip,
                    info?.Severity ?? "-",
                    r.OriginalDetector,
                    string.IsNullOrWhiteSpace(info?.Country) ? "-" : info!.Country!,
                    string.IsNullOrWhiteSpace(info?.Isp) ? "-" : info!.Isp!,
                    string.IsNullOrWhiteSpace(info?.Org) ? "-" : info!.Org!,
                    string.IsNullOrWhiteSpace(info?.ReverseDns) ? "-" : info!.ReverseDns!,
                    r.BlockedAt == default ? "-" : r.BlockedAt.ToLocalTime().ToString("dd/MM HH:mm"),
                    r.Origin.ToString(),
                    r.RetryCount.ToString(),
                    r.LastRetryAt == default ? "-" : r.LastRetryAt.ToLocalTime().ToString("dd/MM HH:mm")
                });
                t.Couleurs.Add(CouleurSeverite(info?.Severity ?? ""));
            }

            return t;
        }

        private static Tableau TableauHistorique(List<IntrusionEvent> history)
        {
            var t = new Tableau
            {
                Titre = $"Historique des intrusions ({history.Count})",
                Colonnes = new[]
                {
                    new Colonne { Titre = "Sens",          Largeur = 34 },
                    new Colonne { Titre = "Date",          Largeur = 58 },
                    new Colonne { Titre = "Sévérité",      Largeur = 62 },
                    new Colonne { Titre = "Origine",       Largeur = 52 },
                    new Colonne { Titre = "Détecteur",     Largeur = 68 },
                    new Colonne { Titre = "IP",            Largeur = 76 },
                    new Colonne { Titre = "Pays",          Largeur = 80 },
                    new Colonne { Titre = "FAI (ASN)",     Largeur = 70 },
                    new Colonne { Titre = "Organisation",  Largeur = 76 },
                    new Colonne { Titre = "Processus",     Largeur = 70 },
                    new Colonne { Titre = "Détails",       Largeur = 140 }
                }
            };

            foreach (var h in history.OrderByDescending(x => x.Timestamp))
            {
                t.Lignes.Add(new[]
                {
                    h.IsOutbound ? "→" : "←",
                    h.Timestamp.ToLocalTime().ToString("dd/MM HH:mm"),
                    h.Severity.ToString(),
                    h.Origin.ToString(),
                    h.Source,
                    h.RemoteIp ?? "-",
                    string.IsNullOrWhiteSpace(h.Country) ? "-" : h.Country!,
                    string.IsNullOrWhiteSpace(h.Isp) ? "-" : h.Isp!,
                    string.IsNullOrWhiteSpace(h.Org) ? "-" : h.Org!,
                    string.IsNullOrWhiteSpace(h.ProcessName) ? "-" : h.ProcessName!,
                    h.Message
                });
                t.Couleurs.Add(CouleurSeverite(h.Severity.ToString()));
            }

            return t;
        }

        private static Tableau TableauAudit(List<IntrusionEvent> audit)
        {
            var t = new Tableau
            {
                Titre = $"Alertes ignorées — IP de confiance ({audit.Count})",
                Colonnes = new[]
                {
                    new Colonne { Titre = "Sens",         Largeur = 34 },
                    new Colonne { Titre = "Date",         Largeur = 58 },
                    new Colonne { Titre = "Sévérité",     Largeur = 62 },
                    new Colonne { Titre = "Origine",      Largeur = 54 },
                    new Colonne { Titre = "Liste",        Largeur = 46 },
                    new Colonne { Titre = "Détecteur",    Largeur = 62 },
                    new Colonne { Titre = "IP",           Largeur = 78 },
                    new Colonne { Titre = "Pays",         Largeur = 82 },
                    new Colonne { Titre = "FAI (ASN)",    Largeur = 74 },
                    new Colonne { Titre = "Organisation", Largeur = 80 },
                    new Colonne { Titre = "Raison",       Largeur = 156 }
                }
            };

            foreach (var a in audit.OrderByDescending(x => x.Timestamp))
            {
                t.Lignes.Add(new[]
                {
                    a.IsOutbound ? "→" : "←",
                    a.Timestamp.ToLocalTime().ToString("dd/MM HH:mm"),
                    a.Severity.ToString(),
                    a.Origin.ToString(),
                    ListeConfiance(a),
                    a.Source,
                    a.RemoteIp ?? "-",
                    string.IsNullOrWhiteSpace(a.Country) ? "-" : a.Country!,
                    string.IsNullOrWhiteSpace(a.Isp) ? "-" : a.Isp!,
                    string.IsNullOrWhiteSpace(a.Org) ? "-" : a.Org!,
                    a.TrustReason ?? a.Message
                });
                t.Couleurs.Add(CouleurSeverite(a.Severity.ToString()));
            }

            return t;
        }

        private static Tableau TableauListeBlanche(List<ProcessTrustInfo> whitelist)
        {
            var t = new Tableau
            {
                Titre = $"Liste blanche des processus ({whitelist.Count})",
                Colonnes = new[]
                {
                    new Colonne { Titre = "Nom",     Largeur = 110 },
                    new Colonne { Titre = "Chemin",  Largeur = 470 },
                    new Colonne { Titre = "Actif",   Largeur = 50 },
                    new Colonne { Titre = "Éditeur", Largeur = 156 }
                }
            };

            foreach (var w in whitelist.OrderBy(x => x.Name))
            {
                t.Lignes.Add(new[]
                {
                    w.Name,
                    w.Path,
                    w.Enabled ? "Oui" : "Non",
                    string.IsNullOrWhiteSpace(w.Publisher) ? "-" : w.Publisher
                });
                t.Couleurs.Add(null);
            }

            return t;
        }

        // ============================================================
        //  Utilitaires
        // ============================================================

        /// <summary>
        /// Nom de la liste qui a fait ignorer l'alerte. Les entrées antérieures au
        /// champ TrustedList ne le portent pas : on le déduit de l'origine, comme le
        /// fait déjà l'interface, sinon la colonne sort vide sur tout l'historique.
        /// </summary>
        private static string ListeConfiance(IntrusionEvent evt)
        {
            if (!string.IsNullOrWhiteSpace(evt.TrustedList)) return evt.TrustedList!;

            return evt.Origin switch
            {
                IpOrigin.Microsoft => "Microsoft",
                IpOrigin.Bot       => "Google",
                _                  => "-"
            };
        }

        /// <summary>
        /// Couleur de la pastille d'une sévérité. Toutes les valeurs de l'énumération
        /// doivent être couvertes : une valeur oubliée sort sans pastille, et la ligne
        /// perd son repère visuel. « Info » prend l'orange de Warning et « High »
        /// le rouge de Critical, comme dans l'interface. « Whitelisted » est blanc à
        /// l'écran — invisible sur du papier — il devient gris clair.
        /// </summary>
        private static XColor? CouleurSeverite(string severite) => severite?.ToLowerInvariant() switch
        {
            "critical"    => XColor.FromArgb(0xC6, 0x28, 0x28),
            "high"        => XColor.FromArgb(0xE5, 0x39, 0x35),
            "warning"     => XColor.FromArgb(0xF5, 0x9E, 0x0B),
            "info"        => XColor.FromArgb(0xF5, 0x9E, 0x0B),
            "tocheck"     => XColor.FromArgb(0xC2, 0x18, 0x5B),
            "crawler"     => XColor.FromArgb(0x1E, 0x88, 0xE5),
            "bot"         => XColor.FromArgb(0x5B, 0xC0, 0xEB),
            "trusted"     => XColor.FromArgb(0x2E, 0x7D, 0x32),
            "safe"        => XColor.FromArgb(0x43, 0xA0, 0x47),
            "whitelisted" => XColor.FromArgb(0x9E, 0x9E, 0x9E),
            _             => XColor.FromArgb(0xAA, 0xAA, 0xAA)
        };

        private static string Tronquer(XGraphics gfx, string texte, XFont police, double largeur)
        {
            if (string.IsNullOrEmpty(texte)) return "";

            if (gfx.MeasureString(texte, police).Width <= largeur) return texte;

            int longueur = texte.Length;

            while (longueur > 1 && gfx.MeasureString(texte.Substring(0, longueur) + "…", police).Width > largeur)
                longueur--;

            return texte.Substring(0, longueur) + "…";
        }

        /// <summary>
        /// Page de statistiques : un bandeau de synthèse, l'activité par heure sur
        /// toute la largeur, puis deux rangées de deux classements.
        ///
        /// Tout est vectoriel — rectangles et texte —, pour rester net à l'impression.
        /// Les libellés de sévérité gardent les noms des tableaux (Critical, High…) :
        /// une page qui renommerait les mêmes données forcerait à faire la correspondance.
        /// </summary>
        private static double DessinerStatistiques(XGraphics gfx, GlobalStats stats, double largeur, double haut)
        {
            gfx.DrawString("Statistiques", PoliceSection, XBrushes.Black, new XPoint(MargeGauche, haut));
            double y = haut + 20;

            // ── Bandeau de synthèse ────────────────────────────────
            PdfGraphiques.BandeauSynthese(gfx, PolicePied, PoliceChiffre,
                new List<(string, string)>
                {
                    ("Alertes enregistrées", stats.TotalAlerts.ToString("N0")),
                    ("IP distinctes",       stats.DistinctIps.ToString("N0")),
                    ("Pays distincts",      stats.DistinctCountries.ToString("N0")),
                    ("Sévérité maximale",   LibelleSeveriteMax(stats.WorstSeverity))
                },
                MargeGauche, y, largeur, 34);

            y += 56;

            // ── Activité par heure, toute la largeur ──────────────
            gfx.DrawString("Activité par heure", PoliceEntete, XBrushes.Black, new XPoint(MargeGauche, y));
            y += 12;

            PdfGraphiques.HistogrammeHeures(gfx, PolicePied, HeuresCompletes(stats.ActivityByHour),
                                           MargeGauche, y + 8, largeur, 60,
                                           XColor.FromArgb(0x42, 0x9E, 0xD9),
                                           XColor.FromArgb(0xC6, 0x28, 0x28));

            // Marge franche sous l'axe : les étiquettes d'heure descendent sous la
            // ligne de base et venaient heurter le titre de la rangée suivante.
            y += 8 + 60 + 20;

            // ── Deux colonnes de classements ──────────────────────
            const double ecart = 26;
            double largeurColonne = (largeur - ecart) / 2;
            double xDroite = MargeGauche + largeurColonne + ecart;

            gfx.DrawString("Répartition par sévérité", PoliceEntete, XBrushes.Black, new XPoint(MargeGauche, y));
            gfx.DrawString("Pays d'origine", PoliceEntete, XBrushes.Black, new XPoint(xDroite, y));
            y += 11;

            PdfGraphiques.BarresHorizontales(gfx, PoliceTexte, PolicePied, BarresSeverites(stats),
                                            MargeGauche, y, largeurColonne, 64);
            PdfGraphiques.BarresHorizontales(gfx, PoliceTexte, PolicePied, BarresPays(stats),
                                            xDroite, y, largeurColonne, 92);

            y += 10 * PdfGraphiques.Pas + 14;

            gfx.DrawString("Détecteurs les plus déclenchés", PoliceEntete, XBrushes.Black, new XPoint(MargeGauche, y));
            gfx.DrawString("Ports locaux les plus visés", PoliceEntete, XBrushes.Black, new XPoint(xDroite, y));
            y += 11;

            PdfGraphiques.BarresHorizontales(gfx, PoliceTexte, PolicePied, BarresDetecteurs(stats),
                                            MargeGauche, y, largeurColonne, 108);
            PdfGraphiques.BarresHorizontales(gfx, PoliceTexte, PolicePied, BarresPorts(stats),
                                            xDroite, y, largeurColonne, 96);

            return y + 10 * PdfGraphiques.Pas;
        }

        /// <summary>Vingt-quatre heures, même si le moteur n'en renvoie qu'une partie.</summary>
        private static List<(int Heure, int Total, int Critiques)> HeuresCompletes(List<HourlyStats> source)
        {
            var parHeure = (source ?? new List<HourlyStats>())
                .GroupBy(h => h.Hour)
                .ToDictionary(g => g.Key, g => g.First());

            var resultat = new List<(int, int, int)>(24);

            for (int heure = 0; heure < 24; heure++)
            {
                var trouvee = parHeure.TryGetValue(heure, out var h) ? h : null;
                resultat.Add((heure, trouvee?.Count ?? 0, trouvee?.CriticalCount ?? 0));
            }

            return resultat;
        }

        private static List<PdfGraphiques.Barre> BarresSeverites(GlobalStats stats)
        {
            // Du plus grave au moins grave, dans l'ordre de l'énumération Severity.
            string[] ordre = { "Critical", "High", "Warning", "ToCheck", "Bot", "Crawler", "Info", "Safe", "Trusted", "Whitelisted" };

            var barres = new List<PdfGraphiques.Barre>();

            foreach (string severite in ordre)
            {
                if (stats.SeverityCounts == null) break;
                if (!stats.SeverityCounts.TryGetValue(severite, out int nombre) || nombre <= 0) continue;

                barres.Add(new PdfGraphiques.Barre(severite, nombre,
                           CouleurSeverite(severite) ?? XColor.FromArgb(0xAA, 0xAA, 0xAA)));
            }

            return barres;
        }

        private static List<PdfGraphiques.Barre> BarresPays(GlobalStats stats)
        {
            var barres = new List<PdfGraphiques.Barre>();

            // Toutes les alertes, comme les sévérités, les détecteurs et les ports.
            if (stats.TopCountries == null) return barres;

            foreach (var pays in stats.TopCountries.Take(10))
            {
                string libelle = string.IsNullOrWhiteSpace(pays.CountryCode)
                    ? pays.Country
                    : pays.CountryCode + " · " + pays.Country;

                barres.Add(new PdfGraphiques.Barre(libelle, pays.Count, XColor.FromArgb(0x37, 0x7D, 0xB3)));
            }

            return barres;
        }

        private static List<PdfGraphiques.Barre> BarresDetecteurs(GlobalStats stats)
        {
            var barres = new List<PdfGraphiques.Barre>();
            if (stats.TopDetectors == null) return barres;

            foreach (var detecteur in stats.TopDetectors.Take(10))
                barres.Add(new PdfGraphiques.Barre(detecteur.Detector, detecteur.Count,
                                                   XColor.FromArgb(0x6A, 0x4C, 0xA8)));

            return barres;
        }

        private static List<PdfGraphiques.Barre> BarresPorts(GlobalStats stats)
        {
            var barres = new List<PdfGraphiques.Barre>();
            if (stats.TopLocalPorts == null) return barres;

            foreach (var port in stats.TopLocalPorts.Take(10))
            {
                string nom = NomPort(port.Port);
                string libelle = string.IsNullOrEmpty(nom) ? port.Port.ToString() : port.Port + " — " + nom;
                barres.Add(new PdfGraphiques.Barre(libelle, port.Count, XColor.FromArgb(0xD8, 0x82, 0x27)));
            }

            return barres;
        }

        /// <summary>Nom usuel d'un port, pour que « 445 » parle à qui lit le rapport.</summary>
        private static string NomPort(int port) => port switch
        {
            21    => "FTP",
            22    => "SSH",
            23    => "Telnet",
            53    => "DNS",
            80    => "HTTP",
            123   => "NTP",
            135   => "RPC",
            139   => "NetBIOS",
            443   => "HTTPS",
            445   => "SMB",
            1433  => "SQL Server",
            3306  => "MySQL",
            3389  => "RDP",
            5900  => "VNC",
            8080  => "HTTP alt.",
            8443  => "HTTPS alt.",
            _     => ""
        };

        private static string LibelleSeveriteMax(string? severite) => severite switch
        {
            "critical" => "Critical",
            "warning"  => "Warning",
            "high"     => "High",
            "ok"       => "Aucune",
            null or "" => "Aucune",
            _          => severite
        };

        private static void DessinerPieds(PdfDocument document, string machineName, List<XGraphics> graphiques)
        {
            int total = document.PageCount;

            for (int i = 0; i < total && i < graphiques.Count; i++)
            {
                var page = document.Pages[i];
                var gfx  = graphiques[i];

                double y = page.Height - 18;

                gfx.DrawLine(new XPen(TraitTableau), MargeGauche, y - 10,
                             page.Width - MargeGauche, y - 10);

                gfx.DrawString($"NetMonitor — {machineName}", PolicePied, XBrushes.Gray,
                               new XPoint(MargeGauche, y));

                string pagination = $"Page {i + 1} / {total}";
                double largeur = gfx.MeasureString(pagination, PolicePied).Width;
                gfx.DrawString(pagination, PolicePied, XBrushes.Gray,
                               new XPoint(page.Width - MargeGauche - largeur, y));
            }
        }
    }
}
