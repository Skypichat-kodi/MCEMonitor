using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Autotrad
{
    public static class XamlParser
    {
        // {loc:Tr 'clé'} ou {loc:Tr "clé"}
        private static readonly Regex _regexLocTr =
            new Regex(@"\{loc:Tr\s*['""]([^'""]+)['""]\}",
                RegexOptions.Compiled);

        // Binding 'clé' Converter={StaticResource Lang}
        private static readonly Regex _regexBindingLang =
            new Regex(@"Binding\s+'([^']+)'.*?Lang",
                RegexOptions.Compiled | RegexOptions.Singleline);

        public static List<XamlEntry> Parse(string[] lines)
        {
            var results = new List<XamlEntry>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                // 1) {loc:Tr 'clé'}
                var mLoc = _regexLocTr.Match(line);
                if (mLoc.Success)
                {
                    results.Add(new XamlEntry
                    {
                        Key = mLoc.Groups[1].Value,
                        LineNumber = i + 1,
                        Raw = line,
                        Preview = line
                    });
                    continue;
                }

                // 2) Binding 'clé' ... Lang
                var mBind = _regexBindingLang.Match(line);
                if (mBind.Success)
                {
                    results.Add(new XamlEntry
                    {
                        Key = mBind.Groups[1].Value,
                        LineNumber = i + 1,
                        Raw = line,
                        Preview = line
                    });
                }
            }

            return results;
        }
    }

    public class XamlEntry
    {
        public string Key { get; set; } = "";
        public int LineNumber { get; set; }
        public string Raw { get; set; } = "";
        public string Preview { get; set; } = "";
    }
}

