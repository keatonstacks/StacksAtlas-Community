using Microsoft.Data.Sqlite;

namespace StacksAtlas.Core.Data.Hub;

public static class SqliteConcurrency
{
    /// <summary>SQLITE_BUSY (5) or SQLITE_LOCKED (6).</summary>
    public static bool IsLockError(SqliteException ex) => ex.SqliteErrorCode is 5 or 6;
}
