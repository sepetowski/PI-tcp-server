using System.Net.Sockets;
using static SocketServerLab1.Server;

namespace SocketServerLab1;

public static class ClientHandler
{

    public static async Task HandleClientConnectionAsync(Socket client, Server server)
    {
        using var cts = new CancellationTokenSource();
        var lastPong = DateTime.UtcNow;
        var buffer = new byte[1024];
        Utils.LogServerMessage($"Client connected: {client.RemoteEndPoint}", ConsoleColor.Green);

        await AuthorizeUser(client, buffer, server);
        _ = PingLoopAsync(client, () => lastPong, server, cts.Token);

        while (true)
        {
            var message = (await Utils.ReadAsync(client, buffer));

            if (message.Equals("PONG", StringComparison.OrdinalIgnoreCase))
            {
                lastPong = DateTime.UtcNow;
                Utils.LogServerMessage($"PONG from {client.RemoteEndPoint}", ConsoleColor.DarkGray);
                continue;
            }

            var parsed = CommandParser.Parse(message);

            if (!parsed.Ok)
            {
                Utils.LogServerMessage($"Command parsing error from {client.RemoteEndPoint}: {message}", ConsoleColor.Yellow);

                switch (parsed.Error)
                {
                    case CommandParser.ParseError.UnknownCommand:
                        await Utils.SendLineAsync(client, "ERR_NOSUCHCOMMAND");
                        break;
                    case CommandParser.ParseError.MissingArgs:
                    case CommandParser.ParseError.TooManyArgs:
                        await Utils.SendLineAsync(client, "ERR_BADREQUEST");
                        break;
                }
                continue;
            }

            switch (parsed.Command)
            {
                case KnewCoammnads.QUIT:
                    await server.QuitSession(client);
                    return;

                case KnewCoammnads.NAME:
                    await Utils.SendLineAsync(client, "ERR_BADREQUEST");
                    break;

                case KnewCoammnads.LIST:
                    await server.ListAllActiveUsers(client);
                    break;

                case KnewCoammnads.MESG:
                    var toId = parsed.Args[0];
                    var text = parsed.Args[1];
                    await server.SendMessageToUserAsync(client, toId, text);
                    break;
            }
        }
    }

    private static async Task PingLoopAsync(Socket client, Func<DateTime> getLastPong, Server server, CancellationToken ct)
    {
        const int pingIntervalMs = 10_000; 
        const int pongTimeoutMs = 5_000;   
        const int maxMissed = 3;           

        int missed = 0;

        while (!ct.IsCancellationRequested)
        {
            DateTime sentAt;

            // 1) Try to send PING
            try
            {
                await Utils.SendLineAsync(client, "PING");
                sentAt = DateTime.UtcNow;
                Utils.LogServerMessage($"PING {client.RemoteEndPoint}", ConsoleColor.DarkGray);
            }
            catch
            {
                // Failed PING
                missed++;
                Utils.LogServerMessage($"Failed PING #{missed} to {client.RemoteEndPoint}", ConsoleColor.Yellow);

                if (missed >= maxMissed)
                {
                    Utils.LogServerMessage($"PING timeout - {client.RemoteEndPoint} removed", ConsoleColor.Red);
                    await server.QuitSession(client, false);
                    return;
                }

                // Wait for next attempt
                try { await Task.Delay(pingIntervalMs, ct); } catch { }
                continue;
            }

            // 2) Wait for PONG
            try { await Task.Delay(pongTimeoutMs, ct); } catch { }

            // 3) Check if PONG arrived after this PING
            if (getLastPong() < sentAt)
            {
                missed++;
                Utils.LogServerMessage($"No PONG #{missed} from {client.RemoteEndPoint}", ConsoleColor.Yellow);

                if (missed >= maxMissed)
                {
                    Utils.LogServerMessage($"PING timeout - {client.RemoteEndPoint} removed", ConsoleColor.Red);
                    await server.QuitSession(client, false);
                    return;
                }
            }
            else
            {
                // PONG received – reset
                if (missed > 0)
                    Utils.LogServerMessage($"PONG recovered after {missed} failed attempts from {client.RemoteEndPoint}", ConsoleColor.DarkGray);

                missed = 0;
            }

            // 4) Delay between PINGs
            try { await Task.Delay(pingIntervalMs, ct); } catch { }
        }
    }

    private static async Task AuthorizeUser(Socket client, byte[] buffer, Server server)
    {
        Utils.LogServerMessage($"Starting client authorization {client.RemoteEndPoint}", ConsoleColor.Cyan);
        await Utils.SendLineAsync(client, "WHO");

        while (true)
        {
            var message = (await Utils.ReadAsync(client, buffer)).Trim();
            if (message.Length == 0)
                await server.QuitSession(client,false);

            var parsed = CommandParser.Parse(message);

            if (!parsed.Ok)
            {
                Utils.LogServerMessage($"Command parsing error during authorization from {client.RemoteEndPoint}: {message}", ConsoleColor.Yellow);

                switch (parsed.Error)
                {
                    case CommandParser.ParseError.UnknownCommand:
                        await Utils.SendLineAsync(client, "WHO");
                        continue;

                    case CommandParser.ParseError.MissingArgs:
                        if (parsed.Command == KnewCoammnads.NAME)
                        {
                            await Utils.SendLineAsync(client, "ERR_NONICKNAMEGIVEN");
                            await Utils.SendLineAsync(client, "WHO");
                        }
                        else
                        {
                            await Utils.SendLineAsync(client, "ERR_BADREQUEST");
                            await Utils.SendLineAsync(client, "WHO");
                        }
                        continue;

                    case CommandParser.ParseError.TooManyArgs:
                        await Utils.SendLineAsync(client, "ERR_BADREQUEST");
                        await Utils.SendLineAsync(client, "WHO");
                        continue;
                }
            }

            switch (parsed.Command)
            {
                case KnewCoammnads.QUIT:
                    await server.QuitSession(client);
                    return;

                case KnewCoammnads.NAME:
                    {
                        var id = (parsed.Args[0] ?? string.Empty).Trim();

                        switch (server.ValidateName(id))
                        {
                            case NameCheck.WrongLength:
                                Utils.LogServerMessage($"Client {client.RemoteEndPoint} provided an ID with invalid length: {id}", ConsoleColor.Red);
                                await Utils.SendLineAsync(client, "WHO");
                                continue;

                            case NameCheck.InUse:
                                Utils.LogServerMessage($"Client {client.RemoteEndPoint} provided an already used ID: {id}", ConsoleColor.Yellow);
                                await Utils.SendLineAsync(client, "ERR_NICKNAMEINUSE");
                                await Utils.SendLineAsync(client, "WHO");
                                continue;

                            case NameCheck.Ok:
                                if (!server.ActiveClients.TryAdd(client, id))
                                {
                                    Utils.LogServerMessage($"Failed to add ID {id} for {client.RemoteEndPoint} (already exists)", ConsoleColor.Red);
                                    await Utils.SendLineAsync(client, "ERR_NICKNAMEINUSE");
                                    await Utils.SendLineAsync(client, "WHO");
                                    continue;
                                }

                                Utils.LogServerMessage($"Authorization completed – {client.RemoteEndPoint} = '{id}'", ConsoleColor.Green);
                                await Utils.SendLineAsync(client, "OK");
                                return; // authorization finished
                        }

                        break;
                    }

                default:
                    Utils.LogServerMessage($"Unexpected command during authorization from {client.RemoteEndPoint}: {parsed.Command}", ConsoleColor.Yellow);
                    await Utils.SendLineAsync(client, "WHO");
                    continue;
            }
        }
    }
}
