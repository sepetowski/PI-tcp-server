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
        Utils.LogServerMessage($"Klient połączony: {client.RemoteEndPoint}", ConsoleColor.Green);

        await AuthorizeUser(client, buffer, server);
        _ = PingLoopAsync(client, () => lastPong, server, cts.Token);

        while (true)
        {
            var message = (await Utils.ReadAsync(client, buffer));

            if (message.Equals("PONG", StringComparison.OrdinalIgnoreCase))
            {
                lastPong = DateTime.UtcNow;
                Utils.LogServerMessage($"PONG od {client.RemoteEndPoint}", ConsoleColor.DarkGray);
                continue;
            }

            var parsed = CommandParser.Parse(message);

            if (!parsed.Ok)
            {
                Utils.LogServerMessage($"Błąd parsowania komendy od {client.RemoteEndPoint}: {message}", ConsoleColor.Yellow);

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
                    await Utils.SendLineAsync(client, "BYE");
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
        const int pingIntervalMs = 10_000; // co 10 s
        const int pongTimeoutMs = 5_000;  // czekamy max 5 s na PONG
        const int maxMissed = 3;      // po 3 braku PONG/nieudanym PING usuwamy klienta

        int missed = 0;

        while (!ct.IsCancellationRequested)
        {
            DateTime sentAt;

            // 1) Spróbuj wysłać PING
            try
            {
                await Utils.SendLineAsync(client, "PING");
                sentAt = DateTime.UtcNow;
                Utils.LogServerMessage($"PING {client.RemoteEndPoint}", ConsoleColor.DarkGray);
            }
            catch
            {
                // Nieudany PING
                missed++;
                Utils.LogServerMessage($"Nieudany PING #{missed} do {client.RemoteEndPoint}", ConsoleColor.Yellow);

                if (missed >= maxMissed)
                {
                    Utils.LogServerMessage($"PING timeout - {client.RemoteEndPoint} usuniety", ConsoleColor.Red);
                    await server.QuitSession(client, false);
                    return;
                }

                // Odczekaj do następnej próby
                try { await Task.Delay(pingIntervalMs, ct); } catch { }
                continue;
            }

            // 2) Czekamy na PONG
            try { await Task.Delay(pongTimeoutMs, ct); } catch { }

            // 3) Sprawdzamy czy PONG przyszedł po tym konkretnym PING
            if (getLastPong() < sentAt)
            {
                missed++;
                Utils.LogServerMessage($"Brak PONG #{missed} od {client.RemoteEndPoint}", ConsoleColor.Yellow);

                if (missed >= maxMissed)
                {
                    Utils.LogServerMessage($"PING timeout - {client.RemoteEndPoint} usuniety", ConsoleColor.Red);
                    await server.QuitSession(client, false);
                    return;
                }
            }
            else
            {
                // PONG wrócił – reset
                if (missed > 0)
                    Utils.LogServerMessage($"PONG odzyskany po {missed} nieudanych próbach od {client.RemoteEndPoint}", ConsoleColor.DarkGray);

                missed = 0;
            }

            // 4) Odstęp między PING-ami
            try { await Task.Delay(pingIntervalMs, ct); } catch { }
        }
    }



    private static async Task AuthorizeUser(Socket client, byte[] buffer, Server server)
    {
        Utils.LogServerMessage($"Rozpoczynam autoryzację klienta {client.RemoteEndPoint}", ConsoleColor.Cyan);
        await Utils.SendLineAsync(client, "WHO");

        while (true)
        {
            var message = (await Utils.ReadAsync(client, buffer)).Trim();
            var parsed = CommandParser.Parse(message);

            if (!parsed.Ok)
            {
                Utils.LogServerMessage($"Błąd parsowania komendy w autoryzacji od {client.RemoteEndPoint}: {message}", ConsoleColor.Yellow);

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
                    Utils.LogServerMessage($"Klient {client.RemoteEndPoint} zakończył połączenie podczas autoryzacji", ConsoleColor.Yellow);
                    await Utils.SendLineAsync(client, "BYE");
                    await server.QuitSession(client);
                    return;

                case KnewCoammnads.NAME:
                    {
                        var id = (parsed.Args[0] ?? string.Empty).Trim();

                        switch (server.ValidateName(id))
                        {
                            case NameCheck.WrongLength:
                                Utils.LogServerMessage($"Klient {client.RemoteEndPoint} podał ID o złej długości: {id}", ConsoleColor.Red);
                                await Utils.SendLineAsync(client, "WHO");
                                continue;

                            case NameCheck.InUse:
                                Utils.LogServerMessage($"Klient {client.RemoteEndPoint} podał zajęty ID: {id}", ConsoleColor.Yellow);
                                await Utils.SendLineAsync(client, "ERR_NICKNAMEINUSE");
                                await Utils.SendLineAsync(client, "WHO");
                                continue;

                            case NameCheck.Ok:
                                if (!server.ActiveClients.TryAdd(client, id))
                                {
                                    Utils.LogServerMessage($"Nie udało się dodać ID {id} dla {client.RemoteEndPoint} (już istnieje)", ConsoleColor.Red);
                                    await Utils.SendLineAsync(client, "ERR_NICKNAMEINUSE");
                                    await Utils.SendLineAsync(client, "WHO");
                                    continue;
                                }

                                Utils.LogServerMessage($"Autoryzacja zakończona – {client.RemoteEndPoint} = '{id}'", ConsoleColor.Green);
                                await Utils.SendLineAsync(client, "OK");
                                return; // autoryzacja zakończona
                        }

                        break;
                    }

                default:
                    Utils.LogServerMessage($"Nieoczekiwana komenda podczas autoryzacji od {client.RemoteEndPoint}: {parsed.Command}", ConsoleColor.Yellow);
                    await Utils.SendLineAsync(client, "WHO");
                    continue;
            }
        }
    }
}
