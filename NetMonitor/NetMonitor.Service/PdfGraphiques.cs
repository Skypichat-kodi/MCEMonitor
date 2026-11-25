using System;
using System.Collections.Generic;
using System.Linq;
using PdfSharpCore.Drawing;

namespace NetMonitor.Service
{
    /// <summary>
    /// Primitives de dessin pour les pages de statistiques du rapport.
    ///
    /// Tout est tracé par XGraphics : rectangles et texte, aucune image. Le rapport
    /// reste donc net à n'importe quel agrandissement, et la couleur ne dépend pas
    /// d'une capture d'écran.
    /// </summary>
    internal static class PdfGraphiques
    {
        internal const double HauteurBarre = 8;
        internal const double EcartBarre = 3.5;

        /// <summary>Pas vertical d'une ligne de barre horizontale.</summary>
        internal static double Pas => HauteurBarre + EcartBarre;

        internal static readonly XColor Trait = XColor.FromArgb(0xD5, 0xD5, 0xD5);
        internal static readonly XColor Texte = XColor.FromArgb(0x3A, 0x3A, 0x3A);
        internal static readonly XColor TextePale = XColor.FromArgb(0x80, 0x80, 0x80);

        /// <summary>Une barre : son libellé, sa valeur et sa couleur.</summary>
        internal readonly struct Barre
        {
            public readonly string Libelle;
            public readonly int Valeur;
            public readonly XColor Couleur;

            public Barre(string libelle, int valeur, XColor couleur)
            {
                Libelle = libelle;
                Valeur = valeur;
                Couleur = couleur;
            }
        }

        /// <summary>
        /// Barres horizontales : libellé à gauche, barre colorée proportionnelle au
        /// maximum, valeur chiffrée à droite.
        ///
        /// Pas de piste grise derrière les barres : imprimée, elle dessine une trame
        /// de lignes horizontales sur toute la page. Les valeurs étant toutes écrites
        /// et toutes les barres parties de la même origine, l'échelle reste lisible.
        /// </summary>
        internal static void BarresHorizontales(
            XGraphics gfx,
            XFont policeLibelle,
            XFont policeValeur,
            IReadOnlyList<Barre> barres,
            double x, double y,
            double largeur, double largeurLibelle)
        {
            if (barres == null || barres.Count == 0)
            {
                gfx.DrawString("Aucune donnée sur la période.", policeLibelle, new XSolidBrush(TextePale),
                               new XPoint(x, y + HauteurBarre));
                return;
            }

            int maximum = Math.Max(1, barres.Max(b => b.Valeur));

            const double largeurValeur = 44;
            double largeurBarre = Math.Max(24, largeur - largeurLibelle - largeurValeur);

            for (int i = 0; i < barres.Count; i++)
            {
                var barre = barres[i];
                double ligneY = y + i * Pas;

                string libelle = Tronquer(gfx, barre.Libelle, policeLibelle, largeurLibelle - 8);
                gfx.DrawString(libelle, policeLibelle, new XSolidBrush(Texte),
                               new XPoint(x, ligneY), XStringFormats.TopLeft);

                double xBarre = x + largeurLibelle;
                double longueur = largeurBarre * (barre.Valeur / (double)maximum);

                if (longueur >= 0.6)
                    gfx.DrawRectangle(new XSolidBrush(barre.Couleur), xBarre, ligneY, longueur, HauteurBarre);

                gfx.DrawString(barre.Valeur.ToString(), policeValeur, new XSolidBrush(Texte),
                               new XPoint(xBarre + largeurBarre + 6, ligneY), XStringFormats.TopLeft);
            }
        }

        /// <summary>
        /// Histogramme vertical des vingt-quatre heures : la hauteur suit le total,
        /// la part critique se dessine en rouge depuis la base. Les heures sans
        /// activité gardent un trait fin, pour que l'axe reste continu.
        /// </summary>
        internal static void HistogrammeHeures(
            XGraphics gfx,
            XFont policeAxe,
            IReadOnlyList<(int Heure, int Total, int Critiques)> heures,
            double x, double y, double largeur, double hauteur,
            XColor couleurNormale, XColor couleurCritique)
        {
            const double ecart = 2;
            int nombre = Math.Max(1, heures.Count);
            double largeurBarre = Math.Max(3, (largeur - (nombre - 1) * ecart) / nombre);

            int maximum = Math.Max(1, heures.Max(h => h.Total));
            double baseY = y + hauteur;

            var pinceauAxe = new XPen(Trait);
            gfx.DrawLine(pinceauAxe, x, baseY, x + largeur, baseY);

            for (int i = 0; i < heures.Count; i++)
            {
                var h = heures[i];
                double xBarre = x + i * (largeurBarre + ecart);

                double hauteurTotale = hauteur * (h.Total / (double)maximum);
                double hauteurCritique = hauteur * (h.Critiques / (double)maximum);

                if (hauteurTotale >= 0.6)
                    gfx.DrawRectangle(new XSolidBrush(couleurNormale),
                                      xBarre, baseY - hauteurTotale, largeurBarre, hauteurTotale);

                if (hauteurCritique >= 0.6)
                    gfx.DrawRectangle(new XSolidBrush(couleurCritique),
                                      xBarre, baseY - hauteurCritique, largeurBarre, hauteurCritique);
            }

            // Étiquettes toutes les six heures : au-delà, elles se chevauchent.
            foreach (int heure in new[] { 0, 6, 12, 18 })
            {
                double xEtiquette = x + heure * (largeurBarre + ecart) + largeurBarre / 2;
                string texte = heure.ToString("00") + "h";
                double demieLargeur = gfx.MeasureString(texte, policeAxe).Width / 2;
                gfx.DrawString(texte, policeAxe, new XSolidBrush(TextePale),
                               new XPoint(xEtiquette - demieLargeur, baseY + 3), XStringFormats.TopLeft);
            }

            string derniere = "23h";
            double largeurDerniere = gfx.MeasureString(derniere, policeAxe).Width;
            double xDerniere = x + largeur - largeurDerniere;
            gfx.DrawString(derniere, policeAxe, new XSolidBrush(TextePale),
                           new XPoint(xDerniere, baseY + 3), XStringFormats.TopLeft);

            // Valeur maximale rappelée au-dessus de l'axe : une échelle sans chiffre
            // ne veut rien dire sur une feuille imprimée.
            gfx.DrawString("max " + maximum, policeAxe, new XSolidBrush(TextePale),
                           new XPoint(x, y - 9));
        }

        /// <summary>
        /// Bandeau de synthèse : quatre chiffres clés posés côte à côte, chacun
        /// sur un fond très clair. Pas de cadre : à l'impression, quatre rectangles
        /// bordés dessinent une grille de traits verticaux et horizontaux qui n'a
        /// aucune information à apporter.
        /// </summary>
        internal static void BandeauSynthese(
            XGraphics gfx,
            XFont policeLibelle, XFont policeChiffre,
            IReadOnlyList<(string Libelle, string Valeur)> cases,
            double x, double y, double largeur, double hauteur)
        {
            if (cases == null || cases.Count == 0) return;

            const double ecartCase = 8;
            double largeurCase = (largeur - (cases.Count - 1) * ecartCase) / cases.Count;
            var pinceauFond = new XSolidBrush(XColor.FromArgb(0xF4, 0xF4, 0xF4));

            for (int i = 0; i < cases.Count; i++)
            {
                double xCase = x + i * (largeurCase + ecartCase);

                gfx.DrawRectangle(pinceauFond, xCase, y, largeurCase, hauteur);

                string libelle = Tronquer(gfx, cases[i].Libelle, policeLibelle, largeurCase - 16);
                gfx.DrawString(libelle, policeLibelle, new XSolidBrush(TextePale),
                               new XPoint(xCase + 8, y + 6), XStringFormats.TopLeft);

                // Le chiffre tient dans la case : posé à +16 en corps 12, son bas
                // tombe à 31, donc sous les 34 points de hauteur.
                gfx.DrawString(cases[i].Valeur, policeChiffre, new XSolidBrush(Texte),
                               new XPoint(xCase + 8, y + 16), XStringFormats.TopLeft);
            }
        }

        /// <summary>
        /// Réduit un texte à la largeur donnée, point de suspension compris.
        /// </summary>
        internal static string Tronquer(XGraphics gfx, string texte, XFont police, double largeur)
        {
            if (string.IsNullOrEmpty(texte)) return "";

            if (gfx.MeasureString(texte, police).Width <= largeur) return texte;

            const string suspension = "…";

            for (int longueur = texte.Length - 1; longueur > 0; longueur--)
            {
                string essai = texte.Substring(0, longueur) + suspension;
                if (gfx.MeasureString(essai, police).Width <= largeur) return essai;
            }

            return suspension;
        }
    }
}
