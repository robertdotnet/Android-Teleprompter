using System.Text.Json;
using System.Text.Json.Serialization;

namespace AndroidTeleprompterRemote.Models
{
    public enum RemoteCommand
    {
        ScrollUp,
        ScrollDown,
        Play,
        Pause,
        Restart
    }

    public sealed record RemoteMessage
    {
        // Keep the JSON payload within BLE's default 20-byte write limit.
        [JsonPropertyName("c")]
        public RemoteCommand Command { get; set; }

        [JsonPropertyName("s")]
        public double? SpeedDps { get; set; }

        public RemoteMessage()
        {
        }

        public RemoteMessage(RemoteCommand command, double? speedDps = null)
        {
            Command = command;
            SpeedDps = speedDps;
        }
    }

    public static class RemoteMessageCodec
    {
        public static byte[] Encode(RemoteMessage msg)
        {
            return JsonSerializer.SerializeToUtf8Bytes(msg);
        }

        public static RemoteMessage? Decode(byte[] data)
        {
            return JsonSerializer.Deserialize<RemoteMessage>(data);
        }
    }
}
