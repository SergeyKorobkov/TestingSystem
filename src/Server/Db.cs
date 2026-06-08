using Npgsql;

namespace Server;

public static class Db
{
    public static NpgsqlDataSource CreateDataSource(string connectionString)
        => NpgsqlDataSource.Create(connectionString);
}
