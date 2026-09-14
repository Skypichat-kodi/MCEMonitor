using System;
using System.Net;
using MediaMonitor.Core.Services;

namespace MediaMonitor.Service.Web.Handlers
{
    /// <summary>
    /// Contexte passé à chaque handler : accès à la requête HTTP,
    /// au moteur et aux paramètres du serveur.
    /// </summary>
    public class HandlerContext
    {
        public HttpListenerContext Http { get; }
        public MediaMonitorEngine Engine { get; }
        public WebServerSettings Settings { get; }
        public int Port { get; }

        public HandlerContext(
            HttpListenerContext http,
            MediaMonitorEngine engine,
            WebServerSettings settings,
            int port)
        {
            Http = http;
            Engine = engine;
            Settings = settings;
            Port = port;
        }
    }
}