using System;
using System.IO;
using MediaMonitor.Service.Web;

namespace MediaMonitor.Service.Web.Handlers
{
    public class ResourceHandler
    {
        private const string IconsDir  = @"C:\ProgramData\MCEMonitor\Resources\Icons";
        private const string ImagesDir = @"C:\ProgramData\MCEMonitor\Resources\Images";
        private const string IcoPath   = @"C:\ProgramData\MCEMonitor\MediaMonitor.ico";

        public void Favicon(HandlerContext ctx)
        {
            if (File.Exists(IcoPath))
            {
                byte[] ico = File.ReadAllBytes(IcoPath);
                HttpWriter.WriteBytes(ctx.Http, ico, "image/x-icon");
                return;
            }
            HttpWriter.Write404(ctx.Http);
        }

        public void Icon(HandlerContext ctx)
        {
            ServeFromDirectory(ctx, IconsDir, "image/png");
        }

        public void Image(HandlerContext ctx)
        {
            ServeFromDirectory(ctx, ImagesDir, "image/png");
        }

        private static void ServeFromDirectory(HandlerContext ctx, string dir, string contentType)
        {
            string path = ctx.Http.Request.Url.AbsolutePath.ToLowerInvariant();
            string fileName = Path.GetFileName(path);
            string fullPath = Path.Combine(dir, fileName);

            if (File.Exists(fullPath))
            {
                byte[] bytes = File.ReadAllBytes(fullPath);
                HttpWriter.WriteBytes(ctx.Http, bytes, contentType);
                return;
            }
            HttpWriter.Write404(ctx.Http);
        }
    }
}