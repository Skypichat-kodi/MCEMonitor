using System.Drawing;

namespace MCEMonitorClient.Config
{
    /// <summary>
    /// Palette de couleurs du client (thème sombre, cohérent avec MCEMonitor).
    /// </summary>
    public static class Theme
    {
        // --- Fond ---
        public static readonly Color Background     = Color.FromArgb(0x1E, 0x1E, 0x1E); // #1E1E1E
        public static readonly Color Panel          = Color.FromArgb(0x25, 0x25, 0x26); // #252526
        public static readonly Color Border         = Color.FromArgb(0x3C, 0x3C, 0x3C); // #3C3C3C

        // --- Texte ---
        public static readonly Color Text           = Color.FromArgb(0xE5, 0xE5, 0xE5); // #E5E5E5
        public static readonly Color TextDim        = Color.FromArgb(0xB4, 0xB4, 0xB4); // #B4B4B4
        public static readonly Color TextGray       = Color.FromArgb(0x88, 0x88, 0x88); // #888888

        // --- Accent ---
        public static readonly Color Accent         = Color.FromArgb(0x4C, 0xC2, 0xFF); // #4CC2FF

        // --- États ---
        public static readonly Color Ok             = Color.FromArgb(0x6C, 0xCB, 0x5F); // #6CCB5F
        public static readonly Color Warning        = Color.FromArgb(0xFF, 0xB9, 0x00); // #FFB900
        public static readonly Color Critical       = Color.FromArgb(0xFF, 0x63, 0x47); // #FF6347

        // --- Fond des lignes du DataGridView ---
        public static readonly Color RowEven        = Color.FromArgb(0x1F, 0x1F, 0x1F);
        public static readonly Color RowOdd         = Color.FromArgb(0x26, 0x26, 0x26);
        public static readonly Color RowSelected    = Color.FromArgb(0x4C, 0xC2, 0xFF);

        // --- Police par défaut ---
        public const string FontFamily = "Segoe UI";
    }
}