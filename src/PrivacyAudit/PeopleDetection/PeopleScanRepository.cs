using Microsoft.Data.Sqlite;

namespace PrivacyAudit.PeopleDetection;

public sealed class PeopleScanRepository
{
    readonly string _connectionString;
    readonly object _initializationGate = new();
    bool _initialized;

    public PeopleScanRepository(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        // Never let a second PrivacyAudit instance wait indefinitely on a locked database.
        _connectionString = $"Data Source={databasePath};Default Timeout=5";
    }

    public PeopleScanResult? FindReusable(string path, long size, DateTime modifiedAt, string modelVersion)
    {
        var result = Get(path);
        return result is not null && result.IsReusable(path, size, modifiedAt, modelVersion) ? result : null;
    }

    public PeopleScanResult? Get(string path)
    {
        EnsureInitialized();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT path,file_size,file_modified_at,model_version,status,people_detected,face_count,max_confidence,scanned_at,error FROM people_scan_results WHERE path=$path";
        command.Parameters.AddWithValue("$path", path);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public void Upsert(PeopleScanResult result)
    {
        EnsureInitialized();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO people_scan_results(path,file_size,file_modified_at,model_version,status,people_detected,face_count,max_confidence,scanned_at,error)
            VALUES($path,$size,$modified,$model,$status,$detected,$faces,$confidence,$scanned,$error)
            ON CONFLICT(path) DO UPDATE SET file_size=$size,file_modified_at=$modified,model_version=$model,status=$status,
            people_detected=$detected,face_count=$faces,max_confidence=$confidence,scanned_at=$scanned,error=$error
            """;
        command.Parameters.AddWithValue("$path", result.Path); command.Parameters.AddWithValue("$size", result.FileSize);
        command.Parameters.AddWithValue("$modified", result.FileModifiedAt.ToString("O")); command.Parameters.AddWithValue("$model", result.ModelVersion);
        command.Parameters.AddWithValue("$status", result.Status.ToString()); command.Parameters.AddWithValue("$detected", result.PeopleDetected ? 1 : 0);
        command.Parameters.AddWithValue("$faces", result.FaceCount); command.Parameters.AddWithValue("$confidence", result.MaxConfidence);
        command.Parameters.AddWithValue("$scanned", result.ScannedAtUtc.ToString("O")); command.Parameters.AddWithValue("$error", result.Error);
        command.ExecuteNonQuery();
    }

    public void DeleteAll() { EnsureInitialized(); using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "DELETE FROM people_scan_results"; command.ExecuteNonQuery(); }

    void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_initializationGate)
        {
            if (_initialized) return;
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS people_scan_results(
                    path TEXT PRIMARY KEY,
                    file_size INTEGER NOT NULL,
                    file_modified_at TEXT NOT NULL,
                    model_version TEXT NOT NULL,
                    status TEXT NOT NULL,
                    people_detected INTEGER NOT NULL,
                    face_count INTEGER NOT NULL,
                    max_confidence REAL NOT NULL,
                    scanned_at TEXT NOT NULL,
                    error TEXT NOT NULL DEFAULT '')
                """;
            command.ExecuteNonQuery();
            _initialized = true;
        }
    }

    SqliteConnection Open() { var connection = new SqliteConnection(_connectionString); connection.Open(); return connection; }

    static PeopleScanResult Read(SqliteDataReader reader) => new(
        reader.GetString(0), Enum.TryParse<PeopleScanStatus>(reader.GetString(4), true, out var status) ? status : PeopleScanStatus.Error,
        reader.GetInt32(5) != 0, reader.GetInt32(6), reader.GetDouble(7), reader.GetString(3),
        DateTime.Parse(reader.GetString(8), null, System.Globalization.DateTimeStyles.RoundtripKind), reader.GetInt64(1),
        DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind), reader.GetString(9));
}
