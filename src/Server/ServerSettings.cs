namespace Server;

public sealed class ServerSettings
{
    public int Port { get; init; } = 9000;

    // Fill user/password/db yourself
    public string ConnectionString { get; init; } =
        "Host=127.0.0.1;Port=5432;Database=testsystem;Username=postgres;Password=ChangeMe;Include Error Detail=true";
}
