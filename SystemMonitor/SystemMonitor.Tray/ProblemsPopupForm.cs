using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MCEMonitor.Languages;

namespace SystemMonitor.Tray
{
    public class ProblemsPopupForm : Form
    {
        public event Action? OnOpenSystemMonitor;
        public event Action? OnOpenMCEMonitor;
        public event Action? OnQuit;

        /// <summary>
        /// L'utilisateur a fermé la ligne d'un crash : elle est acquittée côté service,
        /// ce qui fait retomber l'alerte du Tray sans rien effacer de l'historique.
        /// </summary>
        public event Action<ProblemInfo>? OnAcknowledgeBsod;

        private const int PopupWidth = 340;
        private const int MaxProblemsShown = 5;

        public ProblemsPopupForm(List<ProblemInfo> problems)
        {
            InitializeUI(problems);
        }

        private void InitializeUI(List<ProblemInfo> problems)
        {
            // ------------------------------------------------------------
            //  Paramètres de la fenêtre
            // ------------------------------------------------------------
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.TopMost = true;
            this.BackColor = Color.FromArgb(37, 37, 38);
            this.Padding = new Padding(1);

            // Charge les icônes une seule fois
            Image? iconWarning  = LoadImage("warning.png");
            Image? iconCritical = LoadImage("critical.png");

            // Bordure subtile via Paint
            this.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(60, 60, 60), 1);
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            };

            int y = 12;

            // ------------------------------------------------------------
            //  Titre
            // ------------------------------------------------------------
            var lblTitle = new Label
            {
                Text = "SystemMonitor",
                ForeColor = Color.FromArgb(76, 194, 255),
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Location = new Point(15, y),
                AutoSize = true
            };
            this.Controls.Add(lblTitle);
            y += 35;

            // ------------------------------------------------------------
            //  Séparateur
            // ------------------------------------------------------------
            var sep1 = new Panel
            {
                BackColor = Color.FromArgb(60, 60, 60),
                Location = new Point(0, y),
                Size = new Size(PopupWidth, 1)
            };
            this.Controls.Add(sep1);
            y += 10;

            // ------------------------------------------------------------
            //  Liste des problèmes (max 5)
            // ------------------------------------------------------------
            if (problems == null || problems.Count == 0)
            {
                var lblOk = new Label
                {
                    Text = "[OK]  Aucun problème détecté",
                    ForeColor = Color.FromArgb(108, 203, 95),
                    Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                    Location = new Point(15, y),
                    Size = new Size(PopupWidth - 30, 24),
                    TextAlign = ContentAlignment.MiddleLeft
                };
                this.Controls.Add(lblOk);
                y += 30;
            }
            else
            {
                var shown = problems.Take(MaxProblemsShown).ToList();

                foreach (var p in shown)
                {
                    bool acquittable = p.Category == "BSOD";

                    // Icône PNG selon la sévérité
                    var pic = new PictureBox
                    {
                        Image = p.Severity == "critical" ? iconCritical : iconWarning,
                        SizeMode = PictureBoxSizeMode.Zoom,
                        Location = new Point(15, y + 3),
                        Size = new Size(16, 16),
                        BackColor = Color.Transparent
                    };
                    this.Controls.Add(pic);

                    // Texte du problème (sans marqueur [!] / [?])
                    // Une ligne de crash réserve la place de la croix.
                    int largeurTexte = PopupWidth - 53 - (acquittable ? 22 : 0);

                    var lbl = new Label
                    {
                        Text = FormatProblemText(p),
                        ForeColor = p.Severity == "critical"
                            ? Color.FromArgb(255, 99, 71)
                            : Color.FromArgb(255, 185, 0),
                        Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                        Location = new Point(38, y),
                        Size = new Size(largeurTexte, 22),
                        TextAlign = ContentAlignment.MiddleLeft,
                        AutoEllipsis = true
                    };
                    this.Controls.Add(lbl);

                    if (acquittable)
                        this.Controls.Add(CreerCroix(p, y));

                    y += 24;
                }

                // Indicateur "+N autres"
                if (problems.Count > MaxProblemsShown)
                {
                    var lblMore = new Label
                    {
                        Text = $"... et {problems.Count - MaxProblemsShown} autre(s)",
                        ForeColor = Color.FromArgb(150, 150, 150),
                        Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
                        Location = new Point(15, y),
                        Size = new Size(PopupWidth - 30, 20),
                        TextAlign = ContentAlignment.MiddleLeft
                    };
                    this.Controls.Add(lblMore);
                    y += 22;
                }
            }

            y += 8;

            // ------------------------------------------------------------
            //  Séparateur
            // ------------------------------------------------------------
            var sep2 = new Panel
            {
                BackColor = Color.FromArgb(60, 60, 60),
                Location = new Point(0, y),
                Size = new Size(PopupWidth, 1)
            };
            this.Controls.Add(sep2);
            y += 5;

            // ------------------------------------------------------------
            //  Boutons d'action
            // ------------------------------------------------------------
            y = AddActionButton(LanguageManager.Get("Ouvrir SystemMonitor") ?? "Ouvrir SystemMonitor", y, () =>
            {
                OnOpenSystemMonitor?.Invoke();
                Close();
            });

            y = AddActionButton(LanguageManager.Get("Ouvrir MCEMonitor") ?? "Ouvrir MCEMonitor", y, () =>
            {
                OnOpenMCEMonitor?.Invoke();
                Close();
            });

            y = AddActionButton(LanguageManager.Get("Quitter") ?? "Quitter", y, () =>
            {
                OnQuit?.Invoke();
                Close();
            });

            y += 8;

            // ------------------------------------------------------------
            //  Taille finale
            // ------------------------------------------------------------
            this.ClientSize = new Size(PopupWidth, y);

            // ------------------------------------------------------------
            //  Fermer si clic ailleurs (après un délai pour éviter la fermeture immédiate)
            // ------------------------------------------------------------
            var deactivateTimer = new System.Windows.Forms.Timer { Interval = 200 };
            bool readyToClose = false;

            deactivateTimer.Tick += (s, e) =>
            {
                deactivateTimer.Stop();
                readyToClose = true;
            };

            this.Shown += (s, e) =>
            {
                deactivateTimer.Start();
            };

            this.Deactivate += (s, e) =>
            {
                if (readyToClose)
                    Close();
            };
        }

        /// <summary>
        /// Croix de fermeture d'une ligne de crash : acquitte le BSOD et referme la
        /// popup. C'est le geste par lequel l'utilisateur dit « j'en ai pris
        /// connaissance » ; le crash reste dans l'historique de l'interface.
        /// </summary>
        private Control CreerCroix(ProblemInfo probleme, int y)
        {
            var croix = new Label
            {
                Text = "✕",
                ForeColor = Color.FromArgb(170, 170, 170),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Location = new Point(PopupWidth - 32, y),
                Size = new Size(20, 22),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };

            croix.MouseEnter += (s, e) => croix.ForeColor = Color.FromArgb(255, 99, 71);
            croix.MouseLeave += (s, e) => croix.ForeColor = Color.FromArgb(170, 170, 170);

            croix.Click += (s, e) =>
            {
                OnAcknowledgeBsod?.Invoke(probleme);
                Close();
            };

            new ToolTip().SetToolTip(croix,
                LanguageManager.Get("J'ai pris connaissance de ce crash") ?? "J'ai pris connaissance de ce crash");

            return croix;
        }

        private int AddActionButton(string text, int y, Action onClick)
        {
            var btn = new Button
            {
                Text = text,
                ForeColor = Color.FromArgb(229, 229, 229),
                BackColor = Color.FromArgb(45, 45, 48),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                Location = new Point(10, y),
                Size = new Size(PopupWidth - 20, 32),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
                Cursor = Cursors.Hand
            };

            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 65);
            btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(76, 194, 255);

            btn.Click += (s, e) => onClick();

            this.Controls.Add(btn);
            return y + 36;
        }

        private static string FormatProblemText(ProblemInfo p)
        {
            // Catégorie raccourcie
            string category = p.Category switch
            {
                "CpuHigh"             => "CPU",
                "RamHigh"             => "RAM",
                "TempHigh"            => "Temp",
                "GpuTempHigh"         => "GPU Temp",
                "SmartFailure"        => "SMART",
                "DiskSpaceLow"        => "Disque",
                "DiskSpaceCritical"   => "Disque",
                "BSOD"                => "BSOD",
                _                     => p.Category
            };

            // Message tronqué
            string msg = p.Message;
            if (msg.Length > 55)
                msg = msg.Substring(0, 52) + "...";

            return $"{category}  —  {msg}";
        }

        // ------------------------------------------------------------
        //  Positionnement du popup en bas-droite (zone Tray approximative)
        // ------------------------------------------------------------
        public void ShowAtTrayPosition()
        {
            var screen = Screen.PrimaryScreen!.WorkingArea;

            int x = screen.Right - this.Width - 20;
            int y = screen.Bottom - this.Height - 20;

            this.Location = new Point(x, y);

            this.Show();
            this.Activate();
        }

        // ------------------------------------------------------------
        //  Chargement d'une image PNG (détachée du flux)
        // ------------------------------------------------------------
        private static Image? LoadImage(string fileName)
            => EmbeddedResourceLoader.LoadPngAsImage(fileName);
    }
}