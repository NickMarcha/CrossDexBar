using Microsoft.Data.Sqlite;

namespace CrossdexBar.Providers.Cursor.Tests.Fakes;

internal static class TestVscdb
{
    public static void CreateWithAccessToken(string path, string accessToken)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        using var createTable = connection.CreateCommand();
        createTable.CommandText = "CREATE TABLE ItemTable (key TEXT UNIQUE ON CONFLICT REPLACE, value BLOB);";
        createTable.ExecuteNonQuery();

        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO ItemTable (key, value) VALUES ('cursorAuth/accessToken', $value);";
        insert.Parameters.AddWithValue("$value", accessToken);
        insert.ExecuteNonQuery();
    }
}
