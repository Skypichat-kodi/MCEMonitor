using System;
using System.Windows.Markup;

namespace MediaMonitor.Core.Language
{
    [MarkupExtensionReturnType(typeof(string))]
    public class TrExtension : MarkupExtension
    {
        public string Key { get; set; }

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
            string value = LanguageManager.Get(Key);

            // Si null ? fallback sur la clé brute
            return value ?? Key;
        }
    }
}

