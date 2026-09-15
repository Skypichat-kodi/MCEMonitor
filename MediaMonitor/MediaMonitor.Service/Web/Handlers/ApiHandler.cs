using System;
using MediaMonitor.Service.Web;
using MediaMonitor.Service.Web.Reports;

namespace MediaMonitor.Service.Web.Handlers
{
    public class ApiHandler
    {
        public void History(HandlerContext ctx)
            => HttpWriter.WriteJson(ctx.Http, ctx.Engine.GetHistory());

        public void Live(HandlerContext ctx)
            => HttpWriter.WriteJson(ctx.Http, ctx.Engine.GetCurrentOpenFiles());

        public void LastImage(HandlerContext ctx)
            => HttpWriter.WriteJson(ctx.Http, new { lastImage = ctx.Engine.GetLastImage() });

        public void Status(HandlerContext ctx)
            => HttpWriter.WriteJson(ctx.Http, new
            {
                server  = Environment.MachineName,
                time    = DateTime.Now,
                open    = ctx.Engine.GetCurrentOpenFiles().Count,
                history = ctx.Engine.GetHistory().Count
            });

        public void Report(HandlerContext ctx)
            => HttpWriter.WriteHtml(ctx.Http,
                MediaMonitor.Service.Web.Reports.ReportHtmlBuilder.Build(ctx.Engine.GetHistory()));

        public void Clear(HandlerContext ctx)
        {
            ctx.Engine.ClearHistory();
            HttpWriter.WriteHtml(ctx.Http,
                "<html><body><h2>Historique effacé.</h2></body></html>");
        }
    }
}