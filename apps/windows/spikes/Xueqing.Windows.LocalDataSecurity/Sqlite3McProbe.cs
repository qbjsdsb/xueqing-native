using Microsoft.Data.Sqlite;

namespace Xueqing.Windows.LocalDataSecurity;

public static class Sqlite3McProbe
{
    private static readonly Lazy<bool> RuntimeInitialization = new(
        static () =>
        {
            SQLitePCL.Batteries_V2.Init();
            return true;
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static SqliteConnection CreateConnection(
        string databasePath,
        string password,
        SqliteOpenMode mode = SqliteOpenMode.ReadWriteCreate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        _ = RuntimeInitialization.Value;

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = mode,
            Pooling = false,
            Password = password,
        }.ToString();

        return new SqliteConnection(connectionString);
    }

    public static async Task<string> GetVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite3mc_version();";
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("SQLite3MC did not report a version.");
    }
}
