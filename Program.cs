using SocketServerLab1;

class Program
{
    static async Task Main()
    {
        var server = new Server();
        await server.RunAsync(6000);
    }
}
