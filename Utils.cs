using System.Net.Sockets;
using System.Text;

namespace SocketServerLab1;

public static class Utils
{
    public static Task<int> SendLineAsync(Socket client, string line)
        => client.SendAsync(Encoding.ASCII.GetBytes(line + "\n"), SocketFlags.None);

    public static async Task<string> ReadAsync(Socket client, byte[] buffer)
    {
        var sb = new StringBuilder();

        while (true)
        {
            int bytes = await client.ReceiveAsync(buffer, SocketFlags.None);
            if (bytes == 0) return string.Empty;

            sb.Append(Encoding.ASCII.GetString(buffer, 0, bytes));

            if (sb.ToString().Contains('\n'))
                break;
        }

        return sb.ToString().Trim();
    }

    public static void LogServerMessage(string message, ConsoleColor color = ConsoleColor.Gray)
    {
        lock (typeof(Utils))
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var original = Console.ForegroundColor;

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"[{timestamp}] ");

            Console.ForegroundColor = color;
            Console.WriteLine($"[SERVER] {message}");

            Console.ForegroundColor = original;
        }
    }
}

