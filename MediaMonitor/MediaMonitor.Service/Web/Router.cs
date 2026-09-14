using System;
using System.Collections.Generic;
using System.Net;
using MediaMonitor.Service.Web.Handlers;

namespace MediaMonitor.Service.Web
{
    /// <summary>
    /// Routeur HTTP minimaliste : chemin ? handler.
    /// Supporte les routes exactes et les routes "préfixes" (ex: /resources/).
    /// </summary>
    public class Router
    {
        private readonly Dictionary<string, Action<HandlerContext>> _exactRoutes
            = new(StringComparer.OrdinalIgnoreCase);

        private readonly List<(string Prefix, Action<HandlerContext> Handler)> _prefixRoutes
            = new();

        public void Map(string path, Action<HandlerContext> handler)
        {
            _exactRoutes[path.ToLowerInvariant()] = handler;
        }

        public void MapPrefix(string prefix, Action<HandlerContext> handler)
        {
            _prefixRoutes.Add((prefix.ToLowerInvariant(), handler));
        }

        public bool TryInvoke(HandlerContext ctx, string path)
        {
            // 1) Route exacte
            if (_exactRoutes.TryGetValue(path, out var handler))
            {
                handler(ctx);
                return true;
            }

            // 2) Route par préfixe (ordre d'ajout respecté)
            foreach (var (prefix, h) in _prefixRoutes)
            {
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    h(ctx);
                    return true;
                }
            }

            return false;
        }
    }
}