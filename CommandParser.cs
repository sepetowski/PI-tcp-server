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
    }

    private sealed class Spec
    {
        public KnewCoammnads Cmd { get; }
        public int RequiredArgs { get; }              
        public bool RemainderToLast { get; }            

        public Spec(KnewCoammnads cmd, int requiredArgs, bool remainderToLast = false)
        {
            Cmd = cmd;
            RequiredArgs = requiredArgs;
            RemainderToLast = remainderToLast;
        }
    }

    private static readonly Dictionary<string, Spec> _specs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "QUIT", new Spec(KnewCoammnads.QUIT, 0) },
            { "LIST", new Spec(KnewCoammnads.LIST, 0) },
            { "NAME", new Spec(KnewCoammnads.NAME, 1) },
            { "MESG", new Spec(KnewCoammnads.MESG, 2, remainderToLast: true) },
        };

    public static Result Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return new Result { Ok = false, Error = ParseError.UnknownCommand };

        var tokens = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var verb = tokens[0];

        if (!_specs.TryGetValue(verb, out var spec))
            return new Result { Ok = false, Error = ParseError.UnknownCommand };

        var presentArgs = tokens.Length - 1;

        if (spec.RemainderToLast)
        {
            // Np. MESG: pierwszy argument prosty (id), drugi = reszta linii
            var simpleCount = spec.RequiredArgs - 1;
            if (presentArgs < simpleCount)
                return new Result { Ok = false, Command = spec.Cmd, Error = ParseError.MissingArgs };

            var args = new List<string>(spec.RequiredArgs);

            // proste argumenty (bez spacji)
            for (int i = 0; i < simpleCount; i++)
                args.Add(tokens[1 + i]);

            // reszta linii jako ostatni argument (może mieć spacje)
            var remainderStart = 1 + simpleCount;
            var remainder = remainderStart < tokens.Length
                ? string.Join(' ', tokens, remainderStart, tokens.Length - remainderStart)
                : "";

            if (string.IsNullOrEmpty(remainder))
                return new Result { Ok = false, Command = spec.Cmd, Error = ParseError.MissingArgs };

            args.Add(remainder);
            return new Result { Ok = true, Command = spec.Cmd, Args = args.ToArray() };
        }
        else
        {
            if (presentArgs < spec.RequiredArgs)
                return new Result { Ok = false, Command = spec.Cmd, Error = ParseError.MissingArgs };
            if (presentArgs > spec.RequiredArgs)
                return new Result { Ok = false, Command = spec.Cmd, Error = ParseError.TooManyArgs };

            var args = new string[spec.RequiredArgs];
            Array.Copy(tokens, 1, args, 0, spec.RequiredArgs);
            return new Result { Ok = true, Command = spec.Cmd, Args = args };
        }
    }
}
