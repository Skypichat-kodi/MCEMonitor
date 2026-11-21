using System;
using System.Windows.Markup;

namespace MCEMonitor.Languages
{
    [MarkupExtensionReturnType(typeof(string))]
    public class TrExtension : MarkupExtension
    {
        public string? Key { get; set; }

        /// <summary>Met le texte en majuscules, sans toucher aux traductions.</summary>
        public bool Upper { get; set; }

        public TrExtension() { }

        public TrExtension(string key)
        {
            Key = key;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrWhiteSpace(Key))
                return "";

            // Appel direct à ton LanguageManager
            string? value = LanguageManager.Get(Key);

            // Si null ? fallback sur la clé brute
            string resultat = value ?? Key;

            return Upper ? resultat.ToUpperInvariant() : resultat;
        }
    }
}