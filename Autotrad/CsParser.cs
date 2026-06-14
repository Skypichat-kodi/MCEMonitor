using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Autotrad
{
    public static class CsParser
    {
        private static readonly Regex RegexKey =
            new(@"LanguageManager\.Get\(""(?<key>[^""]+)""\)", RegexOptions.Compiled);

        private static readonly Regex RegexFallbackStart =
            new(@"\?\?", RegexOptions.Compiled);

        private static readonly Regex RegexStringLiteral =
            new(@"(?<!@|\$)""(?<text>(?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

        private static readonly Regex RegexVerbatim =
            new(@"@""(?<text>[^""]*)""", RegexOptions.Compiled);

        private static readonly Regex RegexInterpolation =
            new(@"\$""(?<text>(?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

        private static readonly Regex RegexUiLine =
            new(@"this\.\w+\.(Text|Header|Content|ToolTip|Title)\s*=", RegexOptions.Compiled);

        private static readonly Regex RegexCallWithString =
            new(@"(MessageBox\.Show|Items\.Add|new\s+Exception|new\s+ToolStripMenuItem)\s*\(", RegexOptions.Compiled);

        // ---------------------------------------------------------
        // Extraction arguments
        // ---------------------------------------------------------
        private static List<(string key, string fallback, string preview)> ExtractArguments(string expr)
        {
            var list = new List<(string key, string fallback, string preview)>();

            int start = expr.IndexOf('(');
            int end = expr.LastIndexOf(')');
            if (start < 0 || end < 0 || end <= start)
                return list;

            string inside = expr.Substring(start + 1, end - start - 1);
            var args = inside.Split(',');

            foreach (var rawArg in args)
            {
                string arg = rawArg.Trim();

                var mKey = RegexKey.Match(arg);
                if (mKey.Success)
                {
                    string keyLM = mKey.Groups["key"].Value;

                    string fallbackLM = "";
                    var mFallback = RegexFallbackStart.Match(arg);
                    if (mFallback.Success)
                    {
                        string zoneLM = arg.Substring(mFallback.Index + 2);
                        var mStr = RegexStringLiteral.Match(zoneLM);
                        if (mStr.Success)
                            fallbackLM = Unescape(mStr.Groups["text"].Value);
                    }

                    list.Add((keyLM, fallbackLM, arg));
                    continue;
                }

                var mLiteral = RegexStringLiteral.Match(arg);
                if (mLiteral.Success)
                {
                    string fallback = Unescape(mLiteral.Groups["text"].Value);
                    list.Add(("", fallback, arg));
                    continue;
                }

                var mVerb = RegexVerbatim.Match(arg);
                if (mVerb.Success)
                {
                    string fallback = mVerb.Groups["text"].Value;
                    list.Add(("", fallback, arg));
                    continue;
                }

                var mInterp = RegexInterpolation.Match(arg);
                if (mInterp.Success)
                {
                    string fallback = Unescape(mInterp.Groups["text"].Value);
                    list.Add(("", fallback, arg));
                    continue;
                }
            }

            return list;
        }

        // ---------------------------------------------------------
        // Parse global
        // ---------------------------------------------------------
        public static List<CsEntry> Parse(string[] lines)
        {
            var results = new List<CsEntry>();
            var used = new HashSet<string>();

            var blocks = ExtractBlocks(lines);

            foreach (var block in blocks)
            {
                int baseLine = block.startLine;
                string[] bLines = block.content;

                // UI
                for (int i = 0; i < bLines.Length; i++)
                {
                    if (RegexUiLine.IsMatch(bLines[i]))
                    {
                        string expr = ExtractExpression(bLines, i);
                        var entries = ParseExpression(expr, baseLine + i);

                        foreach (var e in entries)
                            if (used.Add($"{e.LineNumber}|{e.Fallback}"))
                                results.Add(e);
                    }
                }

                // Appels
                for (int i = 0; i < bLines.Length; i++)
                {
                    if (RegexCallWithString.IsMatch(bLines[i]))
                    {
                        string expr = ExtractExpression(bLines, i);
                        var entries = ParseExpression(expr, baseLine + i);

                        foreach (var e in entries)
                            if (used.Add($"{e.LineNumber}|{e.Fallback}"))
                                results.Add(e);
                    }
                }
            }

            return results;
        }

        // ---------------------------------------------------------
        // Extraction blocs
        // ---------------------------------------------------------
        private static List<(int startLine, string[] content)> ExtractBlocks(string[] lines)
        {
            var blocks = new List<(int, string[])>();

            int depth = 0;
            int start = -1;
            var buffer = new List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i];

                if (l.Contains("{"))
                {
                    if (depth == 0)
                    {
                        start = i + 1;
                        buffer.Clear();
                    }
                    depth++;
                }

                if (depth > 0)
                    buffer.Add(l);

                if (l.Contains("}"))
                {
                    depth--;
                    if (depth == 0)
                        blocks.Add((start, buffer.ToArray()));
                }
            }

            return blocks;
        }

        // ---------------------------------------------------------
        // Extraction expression multi-ligne
        // ---------------------------------------------------------
        private static string ExtractExpression(string[] lines, int index)
        {
            string expr = lines[index];
            int i = index + 1;

            while (!Regex.IsMatch(expr, @";\s*$") && i < lines.Length)
            {
                expr += "\n" + lines[i];
                i++;
            }

            return expr;
        }

        // ---------------------------------------------------------
        // ParseExpression
        // ---------------------------------------------------------
        private static List<CsEntry> ParseExpression(string expr, int lineNumber)
        {
            var list = new List<CsEntry>();

            // ---------------------------------------------------------
            // PATCH : FUSION DES LITTÉRAUX MULTI-LIGNES
            // ---------------------------------------------------------
            var allLiterals = Regex.Matches(expr,
                "\"((?:[^\"\\\\]|\\\\.)*)\"",
                RegexOptions.Singleline);

            // ? NE PAS fusionner pour les appels (MessageBox.Show, Items.Add, Exception…)
            if (allLiterals.Count > 1 && !RegexCallWithString.IsMatch(expr))
            {
                var sb = new System.Text.StringBuilder();

                foreach (Match m in allLiterals)
                {
                    string part = m.Groups[1].Value;

                    if (expr.Contains($"LanguageManager.Get(\"{part}\")"))
                        continue;

                    part = part
                        .Replace("\\n", "\n")
                        .Replace("\\r", "\r")
                        .Replace("\\t", "\t")
                        .Replace("\\\"", "\"")
                        .Replace("\\\\", "\\");

                    part = part.Trim();

                    sb.Append(part);
                    sb.Append("\n");
                }

                string merged = sb.ToString().TrimEnd('\n');

                var mKey = RegexKey.Match(expr);
                string key = mKey.Success ? mKey.Groups["key"].Value : "";

                string preview = expr.Replace("\n", " ").Trim();

                list.Add(new CsEntry
                {
                    LineNumber = lineNumber,
                    Raw = preview,
                    Key = key,
                    Fallback = merged,
                    Preview = preview
                });

                return list;
            }

            // ---------------------------------------------------------
            // Multi-arguments (INCLUS MessageBox.Show)
            // ---------------------------------------------------------
            if (expr.Contains("MessageBox.Show(") ||
                expr.Contains("Items.Add(") ||
                expr.Contains("new Exception(") ||
                expr.Contains("new ToolStripMenuItem("))
            {
                var args = ExtractArguments(expr);

                foreach (var a in args)
                {
                    if (!string.IsNullOrWhiteSpace(a.fallback))
                    {
                        list.Add(new CsEntry
                        {
                            LineNumber = lineNumber,
                            Raw = expr.Replace("\n", " "),
                            Key = a.key,
                            Fallback = a.fallback,
                            Preview = a.preview
                        });
                    }
                }

                if (list.Count > 0)
                    return list;
            }

            // ---------------------------------------------------------
            // LM.Get + concaténation
            // ---------------------------------------------------------
            if (expr.Contains("LanguageManager.Get(") && expr.Contains("+"))
            {
                var entries = new List<CsEntry>();

                var mKey = RegexKey.Match(expr);
                if (mKey.Success)
                {
                    string keyLM = mKey.Groups["key"].Value;

                    string fallbackLM = "";
                    var mFallback = RegexFallbackStart.Match(expr);
                    if (mFallback.Success)
                    {
                        string zoneLM = expr.Substring(mFallback.Index + 2);
                        var mStr = RegexStringLiteral.Match(zoneLM);
                        if (mStr.Success)
                            fallbackLM = Unescape(mStr.Groups["text"].Value);
                    }

                    entries.Add(new CsEntry
                    {
                        LineNumber = lineNumber,
                        Raw = expr.Replace("\n", " "),
                        Key = keyLM,
                        Fallback = fallbackLM,
                        Preview = $"LanguageManager.Get(\"{keyLM}\") ?? \"{fallbackLM}\""
                    });
                }

                string exprWithoutLM = RegexKey.Replace(expr, "");
                exprWithoutLM = RegexFallbackStart.Replace(exprWithoutLM, "");

                int verbatimIndex = exprWithoutLM.IndexOf("@\"");
                if (verbatimIndex >= 0)
                    exprWithoutLM = exprWithoutLM.Substring(0, verbatimIndex);

                var parts = new List<string>();

                foreach (Match m in RegexStringLiteral.Matches(exprWithoutLM))
                    parts.Add(Unescape(m.Groups["text"].Value));

                if (parts.Count > 0 && entries.Count > 0)
                {
                    string fallbackLM = entries[0].Fallback;
                    if (!string.IsNullOrEmpty(fallbackLM) && parts[0] == fallbackLM)
                        parts.RemoveAt(0);
                }

                if (parts.Count > 0)
                {
                    string fallbackRest = string.Join("", parts);

                    entries.Add(new CsEntry
                    {
                        LineNumber = lineNumber,
                        Raw = expr.Replace("\n", " "),
                        Key = "",
                        Fallback = fallbackRest,
                        Preview = fallbackRest
                    });
                }

                if (entries.Count > 0)
                    return entries;
            }

            // ---------------------------------------------------------
            // Comportement original
            // ---------------------------------------------------------
            var keyMatch = RegexKey.Match(expr);
            string keyFinal = keyMatch.Success ? keyMatch.Groups["key"].Value : "";

            string zoneFinal = expr;

            if (!string.IsNullOrEmpty(keyFinal))
            {
                var m = RegexFallbackStart.Match(expr);
                if (m.Success)
                    zoneFinal = expr.Substring(m.Index + 2);
            }

            var parts2 = new List<string>();

            foreach (Match m in RegexStringLiteral.Matches(zoneFinal))
                parts2.Add(Unescape(m.Groups["text"].Value));

            foreach (Match m in RegexVerbatim.Matches(zoneFinal))
                parts2.Add(m.Groups["text"].Value);

            foreach (Match m in RegexInterpolation.Matches(zoneFinal))
                parts2.Add(Unescape(m.Groups["text"].Value));

            if (parts2.Count == 0)
                return list;

            string fallback2 = string.Join("", parts2);

            if (string.IsNullOrWhiteSpace(fallback2))
                return list;

            list.Add(new CsEntry
            {
                LineNumber = lineNumber,
                Raw = expr.Replace("\n", " "),
                Key = keyFinal,
                Fallback = fallback2,
                Preview = BuildPreview(expr, keyFinal, fallback2)
            });

            return list;
        }

        private static string BuildPreview(string expr, string key, string fallback)
        {
            expr = expr.Trim();

            if (!string.IsNullOrEmpty(key))
            {
                var m = Regex.Match(expr,
                    @"LanguageManager\.Get\(""[^""]+""\)\s*\?\?\s*""[^""]*""");

                if (m.Success)
                    return m.Value;
            }

            var assign = Regex.Match(expr, @"this\.\w+\.\w+\s*=\s*.*");
            if (assign.Success)
                return assign.Value.Trim().TrimEnd(';');

            return expr;
        }

        private static string Unescape(string s)
        {
            return s
                .Replace("\\\"", "\"")
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\\\", "\\");
        }
    }

    public class CsEntry
    {
        public int LineNumber { get; set; }
        public string Raw { get; set; } = "";
        public string Key { get; set; } = "";
        public string Fallback { get; set; } = "";
        public string Preview { get; set; } = "";
    }
}

