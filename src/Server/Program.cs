using System.Net;
using System.Net.Sockets;
using System.Text;
using Server;
using Shared;

var settings = new ServerSettings();

Console.WriteLine("Test System Server");
Console.WriteLine("Transport: TCP, protocol: JSON envelope per line");
Console.WriteLine($"Port: {settings.Port}");
Console.WriteLine();

var ds = Db.CreateDataSource(settings.ConnectionString);
var router = new RequestRouter(ds);

var listener = new TcpListener(IPAddress.Any, settings.Port);
listener.Start();
Console.WriteLine($"Listening on 0.0.0.0:{settings.Port}");

while (true)
{
    var client = await listener.AcceptTcpClientAsync();
    _ = Task.Run(() => HandleClientAsync(client));
}

async Task HandleClientAsync(TcpClient client)
{
    Console.WriteLine("Client connected");
    try
    {
        using var c = client;
        using var stream = c.GetStream();

        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        var session = new SessionState();
        using var cts = new CancellationTokenSource();

        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var env = Protocol.Unpack(line);
            var responseLine = await router.HandleAsync(session, env.Type, env.Payload, cts.Token);
            await writer.WriteLineAsync(responseLine);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("Client error: " + ex.Message);
    }
    finally
    {
        Console.WriteLine("Client disconnected");
    }
}
