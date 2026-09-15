using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace MediaMonitor.Service.Ipc
{
    /// <summary>
    /// Helpers pour répondre aux commandes IPC.
    /// </summary>
    public static class IpcResponse
    {
        public static void Json(StreamWriter writer, NamedPipeServerStream server, object data)
        {
            string json = JsonSerializer.Serialize(data);
            writer.Write(json);
            writer.Flush();
            server.WaitForPipeDrain();
            server.Disconnect();
        }

        public static void Raw(StreamWriter writer, NamedPipeServerStream server, string raw)
        {
            writer.Write(raw);
            writer.Flush();
            server.WaitForPipeDrain();
            server.Disconnect();
        }

        public static void Ok(StreamWriter writer, NamedPipeServerStream server)
        {
            Raw(writer, server, "{\"status\":\"ok\"}");
        }

        public static void OkWithMessage(StreamWriter writer, NamedPipeServerStream server, string message)
        {
            Json(writer, server, new { status = "ok", message = message });
        }

        public static void Error(StreamWriter writer, NamedPipeServerStream server, string message)
        {
            Json(writer, server, new { status = "error", message = message });
        }
    }
}