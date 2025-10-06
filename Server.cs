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

        Utils.LogServerMessage($"Nasłuchuję na porcie {port}.", ConsoleColor.Cyan);
        Utils.LogServerMessage("Czekam na połączenie klienta.", ConsoleColor.DarkGray);

        while (true)
        {
            var client = await serverSocket.AcceptAsync();
            Utils.LogServerMessage($"Nowe połączenie od {client.RemoteEndPoint}", ConsoleColor.Green);
            _ = Task.Run(() => ClientHandler.HandleClientConnectionAsync(client, this));
        }
    }

    internal async Task<bool> QuitSession(Socket client, bool disconnectedByUser = true)
    {
        try
        {
            if (ActiveClients.TryGetValue(client, out var name))
            {
                client.Shutdown(SocketShutdown.Receive);

                if (disconnectedByUser)
                {
                    Utils.LogServerMessage($"Klient '{name}' ({client.RemoteEndPoint}) rozłączył się.", ConsoleColor.Yellow);
                    await Utils.SendLineAsync(client, "BYE");
                }

                ActiveClients.Remove(client);
                client.Close();
                return true;
            }
            else
            {
                throw new Exception("Brak klienta");
            }
        }
        catch (Exception ex)
        {
            Utils.LogServerMessage($"Błąd podczas rozłączania {client.RemoteEndPoint}: {ex.Message}", ConsoleColor.Red);
            return false;
        }
    }

    internal async Task<bool> ClientDisconnected(Socket client)
    {
        try
        {
            if (ActiveClients.TryGetValue(client, out var name))
            {
                ActiveClients.Remove(client);
                Utils.LogServerMessage($"Klient '{name}' ({client.RemoteEndPoint}) rozłączył się.", ConsoleColor.Yellow);
            }


            await Utils.SendLineAsync(client, "BYE");
            client.Close();

            return true;
        }
        catch (Exception ex)
        {
            Utils.LogServerMessage($"Błąd podczas rozłączania {client.RemoteEndPoint}: {ex.Message}", ConsoleColor.Red);
            return false;
        }
    }

    internal async Task ListAllActiveUsers(Socket client)
    {
        Utils.LogServerMessage($"Klient {client.RemoteEndPoint} poprosił o listę aktywnych użytkowników.", ConsoleColor.Cyan);

        foreach (var name in ActiveClients.Values)
        {
            await Utils.SendLineAsync(client, name);
        }

        await Utils.SendLineAsync(client, "END");
        Utils.LogServerMessage($"Lista aktywnych użytkowników wysłana do {client.RemoteEndPoint}", ConsoleColor.DarkGray);
    }

    internal async Task SendMessageToUserAsync(Socket client, string target, string message)
    {
        var targetClient = ActiveClients.FirstOrDefault(x => x.Value == target).Key;
        var text = (message ?? string.Empty).Trim();

        if (targetClient == null)
        {
            Utils.LogServerMessage($"Brak uzytkownika o id {target}", ConsoleColor.Red);
            await Utils.SendLineAsync(client, "ERR_BADREQUEST");
            return;
        }

        if (text.Length == 0)
        {
            Utils.LogServerMessage($"Nieprawidłowa wiadomość od {client.RemoteEndPoint} do {target}", ConsoleColor.Red);
            await Utils.SendLineAsync(client, "ERR_BADREQUEST");
            return;
        }

        if (text.Length > 256)
        {
            Utils.LogServerMessage($"Zbyt długa wiadomość od {client.RemoteEndPoint} (>{text.Length} znaków)", ConsoleColor.Red);
            await Utils.SendLineAsync(client, "ERR_MESSAGETOOLARGE");
            return;
        }

        if (!ActiveClients.TryGetValue(client, out var senderId) || string.IsNullOrWhiteSpace(senderId))
        {
            Utils.LogServerMessage($"Klient {client.RemoteEndPoint} próbował wysłać wiadomość bez ID", ConsoleColor.Red);
            await Utils.SendLineAsync(client, "ERR_BADREQUEST");
            return;
        }

        text = text.Replace('\r', ' ').Replace('\n', ' ');
        var ascii = Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(text));

        try
        {
            await Utils.SendLineAsync(targetClient, $"FROM {senderId} {ascii}");
            await Utils.SendLineAsync(client, "OK");
            Utils.LogServerMessage($"Wiadomość od '{senderId}' do '{target}' wyslana", ConsoleColor.Magenta);
        }
        catch (Exception ex)
        {
            Utils.LogServerMessage($"Błąd wysyłania wiadomości do '{target}' ({client.RemoteEndPoint}): {ex.Message}", ConsoleColor.Red);
            await Utils.SendLineAsync(client, "ERR_TIMEOUT");
        }
    }

    internal NameCheck ValidateName(string id)
    {
        if (id.Length != 8)
        {
            Utils.LogServerMessage($"Odrzucono ID '{id}' – niepoprawna długość ({id.Length}).", ConsoleColor.Red);
            return NameCheck.WrongLength;
        }

        if (!CanUseName(id))
        {
            Utils.LogServerMessage($"Odrzucono ID '{id}' – już w użyciu.", ConsoleColor.Yellow);
            return NameCheck.InUse;
        }

        Utils.LogServerMessage($"ID '{id}' jest dostępne.", ConsoleColor.Green);
        return NameCheck.Ok;
    }

    internal bool CanUseName(string name) => !ActiveClients.ContainsValue(name);
}
