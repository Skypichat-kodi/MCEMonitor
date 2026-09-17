using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace RomMonitor.Service.Ipc
{
    public static class IpcResponse
    {
        public static void Json(NamedPipeServerStream server, object data)
        {
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            Raw(server, json);
        }

        public static void Raw(NamedPipeServerStream server, string raw)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(raw);

            server.Write(bytes, 0, bytes.Length);
            server.Flush();
            server.WaitForPipeDrain();
            server.Disconnect();
        }

        public static void Ok(NamedPipeServerStream server)
        {
            Raw(server, "{\"status\":\"ok\"}");
        }

        public static void Error(NamedPipeServerStream server, string message)
        {
            Json(server, new { status = "error", message });
        }
    }
}