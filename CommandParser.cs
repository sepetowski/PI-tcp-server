namespace SocketServerLab1;

public static class CommandParser
{
    public enum ParseError { None, UnknownCommand, MissingArgs, TooManyArgs }

    public readonly struct Result
    {
        public bool Ok { get; init; }
        public KnewCoammnads Command { get; init; }
        public string[] Args { get; init; }
        public ParseError Error { get; init; }

        public static Result ErrorResult(ParseError e, KnewCoammnads cmd = default) =>
            new() { Ok = false, Error = e, Command = cmd, Args = Array.Empty<string>() };

        public static Result OkResult(KnewCoammnads cmd, params string[] args) =>
            new() { Ok = true, Command = cmd, Args = args ?? Array.Empty<string>() };
    }

    public static Result Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return Result.ErrorResult(ParseError.UnknownCommand);

        line = line.Trim();

        // Extract verb
        int space = IndexOfSpace(line);
        string verb = (space < 0 ? line : line[..space]).Trim();
        string rest = (space < 0 ? "" : line[(space + 1)..]).Trim();

        switch (verb.ToUpperInvariant())
        {
            case "QUIT":
                return ParseZeroArgs(rest, KnewCoammnads.QUIT);

            case "LIST":
                return ParseZeroArgs(rest, KnewCoammnads.LIST);

            case "NAME":
                return ParseExactlyOneArg(rest, KnewCoammnads.NAME);

            case "MESG":
                return ParseMesg(rest);

            default:
                return Result.ErrorResult(ParseError.UnknownCommand);
        }
    }

    private static Result ParseZeroArgs(string rest, KnewCoammnads cmd)
    {
        if (string.IsNullOrEmpty(rest)) return Result.OkResult(cmd);
        return Result.ErrorResult(ParseError.TooManyArgs, cmd);
    }

    private static Result ParseExactlyOneArg(string rest, KnewCoammnads cmd)
    {
        if (string.IsNullOrEmpty(rest))
            return Result.ErrorResult(ParseError.MissingArgs, cmd);

        // Take first token as the only arg
        int space = IndexOfSpace(rest);
        if (space < 0) return Result.OkResult(cmd, rest);

        // More than one token → too many
        return Result.ErrorResult(ParseError.TooManyArgs, cmd);
    }

    private static Result ParseMesg(string rest)
    {
        // Needs at least: <toId> <message...>
        if (string.IsNullOrEmpty(rest))
            return Result.ErrorResult(ParseError.MissingArgs, KnewCoammnads.MESG);

        // First token = recipient ID
        int space = IndexOfSpace(rest);
        if (space < 0)
            return Result.ErrorResult(ParseError.MissingArgs, KnewCoammnads.MESG);

        string toId = rest[..space].Trim();
        string message = rest[(space + 1)..].Trim();

        if (string.IsNullOrEmpty(toId) || string.IsNullOrEmpty(message))
            return Result.ErrorResult(ParseError.MissingArgs, KnewCoammnads.MESG);

        return Result.OkResult(KnewCoammnads.MESG, toId, message);
    }

    private static int IndexOfSpace(string s)
    {
        for (int i = 0; i < s.Length; i++)
            if (char.IsWhiteSpace(s[i])) return i;
        return -1;
    }
}
