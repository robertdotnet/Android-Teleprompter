using System.Text.Json;

namespace AndroidTeleprompter.Models
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
        public RemoteCommand Command { get; set; }
        public double? SpeedDps { get; set; }

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