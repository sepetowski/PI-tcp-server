using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SocketServerLab1;

public class Server
{
    internal enum NameCheck { Ok, WrongLength, InUse }
    internal readonly Dictionary<Socket, string> ActiveClients = new();

    public async Task RunAsync(int port)
    {
        var serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var localEndPoint = new IPEndPoint(IPAddress.Any, port);

        serverSocket.Bind(localEndPoint);
        serverSocket.Listen(100);

        Utils.LogServerMessage($"Listening on port {port}.", ConsoleColor.Cyan);
        Utils.LogServerMessage("Waiting for a client connection.", ConsoleColor.DarkGray);

        while (true)
        {
            var client = await serverSocket.AcceptAsync();
            Utils.LogServerMessage($"New connection from {client.RemoteEndPoint}", ConsoleColor.Green);
            _ = Task.Run(() => ClientHandler.HandleClientConnectionAsync(client, this));
        }
    }

    internal async Task QuitSession(Socket client, bool sendMessageToClient = true)
    {
        client.Shutdown(SocketShutdown.Receive);

        if (ActiveClients.TryGetValue(client, out var _))
        {
            if (sendMessageToClient)
                await Utils.SendLineAsync(client, "BYE");


            ActiveClients.Remove(client);
        }
        Utils.LogServerMessage($"Client {client.RemoteEndPoint} disconnected.", ConsoleColor.Yellow);
        client.Close();
    }


    internal async Task ListAllActiveUsers(Socket client)
    {

        foreach (var name in ActiveClients.Values)
        {
            await Utils.SendLineAsync(client, name);
        }

        Utils.LogServerMessage($"List of active users sent to {client.RemoteEndPoint}", ConsoleColor.DarkGray);
    }

    internal async Task SendMessageToUserAsync(Socket client, string target, string message)
    {
        var targetClient = ActiveClients.FirstOrDefault(x => x.Value == target).Key;
        var text = (message ?? string.Empty).Trim();

        if (targetClient == null)
        {
            Utils.LogServerMessage($"No user with id {target}", ConsoleColor.Red);
            await Utils.SendLineAsync(client, "ERR_BADREQUEST");
            return;
        }

        if (text.Length == 0)
        {
            Utils.LogServerMessage($"Invalid message from {client.RemoteEndPoint} to {target}", ConsoleColor.Red);
            await Utils.SendLineAsync(client, "ERR_BADREQUEST");
            return;
        }

        if (text.Length > 256)
        {
            Utils.LogServerMessage($"Message too long from {client.RemoteEndPoint} (>{text.Length} characters)", ConsoleColor.Red);
            await Utils.SendLineAsync(client, "ERR_MESSAGETOOLARGE");
            return;
        }

        if (!ActiveClients.TryGetValue(client, out var senderId) || string.IsNullOrWhiteSpace(senderId))
        {
            Utils.LogServerMessage($"Client {client.RemoteEndPoint} tried to send a message without ID", ConsoleColor.Red);
            await Utils.SendLineAsync(client, "ERR_BADREQUEST");
            return;
        }

        text = text.Replace('\r', ' ').Replace('\n', ' ');
        var ascii = Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(text));

        try
        {
            await Utils.SendLineAsync(targetClient, $"FROM {senderId} {ascii}");
            await Utils.SendLineAsync(client, "OK");
            Utils.LogServerMessage($"Message from '{senderId}' to '{target}' sent", ConsoleColor.Magenta);
        }
        catch (Exception ex)
        {
            Utils.LogServerMessage($"Error sending message to '{target}' ({client.RemoteEndPoint}): {ex.Message}", ConsoleColor.Red);
            await Utils.SendLineAsync(client, "ERR_TIMEOUT");
        }
    }

    internal NameCheck ValidateName(string id)
    {
        if (id.Length != 8)
        {
            Utils.LogServerMessage($"Rejected ID '{id}' – invalid length ({id.Length}).", ConsoleColor.Red);
            return NameCheck.WrongLength;
        }

        if (!CanUseName(id))
        {
            Utils.LogServerMessage($"Rejected ID '{id}' – already in use.", ConsoleColor.Yellow);
            return NameCheck.InUse;
        }

        Utils.LogServerMessage($"ID '{id}' is available.", ConsoleColor.Green);
        return NameCheck.Ok;
    }

    internal bool CanUseName(string name) => !ActiveClients.ContainsValue(name);
}
