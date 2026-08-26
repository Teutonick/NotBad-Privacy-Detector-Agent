using Microsoft.Data.Sqlite;

namespace PrivacyAudit.PeopleDetection;

public sealed class ImageSafetyRepository
{
    readonly string _connectionString;
    readonly object _gate = new();
    bool _initialized;
    public ImageSafetyRepository(string databasePath) { Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!); _connectionString = $"Data Source={databasePath};Default Timeout=5"; }

    public ImageSafetyScanResult? FindReusable(string path, long size, DateTime modifiedAt, string modelVersion)
    { var result = Get(path); return result?.IsReusable(path, size, modifiedAt, modelVersion) == true ? result : null; }

    public ImageSafetyScanResult? Get(string path)
    {
        EnsureInitialized(); using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT path,file_size,file_modified_at,model_version,status,primary_class,nsfl_score,nsfw_score,sfw_score,scanned_at,error FROM image_safety_results WHERE path=$path";
        command.Parameters.AddWithValue("$path", path); using var reader = command.ExecuteReader(); return reader.Read() ? Read(reader) : null;
    }

    public void Upsert(ImageSafetyScanResult result)
    {
        EnsureInitialized(); using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO image_safety_results(path,file_size,file_modified_at,model_version,status,primary_class,nsfl_score,nsfw_score,sfw_score,scanned_at,error)
            VALUES($path,$size,$modified,$model,$status,$class,$nsfl,$nsfw,$sfw,$scanned,$error)
            ON CONFLICT(path) DO UPDATE SET file_size=$size,file_modified_at=$modified,model_version=$model,status=$status,
            primary_class=$class,nsfl_score=$nsfl,nsfw_score=$nsfw,sfw_score=$sfw,scanned_at=$scanned,error=$error
            """;
        command.Parameters.AddWithValue("$path", result.Path); command.Parameters.AddWithValue("$size", result.FileSize);
        command.Parameters.AddWithValue("$modified", result.FileModifiedAt.ToString("O")); command.Parameters.AddWithValue("$model", result.ModelVersion);
        command.Parameters.AddWithValue("$status", result.Status.ToString()); command.Parameters.AddWithValue("$class", result.PrimaryClass.ToString());
        command.Parameters.AddWithValue("$nsfl", result.NsflScore); command.Parameters.AddWithValue("$nsfw", result.NsfwScore); command.Parameters.AddWithValue("$sfw", result.SfwScore);
        command.Parameters.AddWithValue("$scanned", result.ScannedAtUtc.ToString("O")); command.Parameters.AddWithValue("$error", result.Error); command.ExecuteNonQuery();
    }

    void EnsureInitialized()
    {
        if (_initialized) return; lock (_gate) { if (_initialized) return; using var connection = Open(); using var command = connection.CreateCommand();
            command.CommandText = """CREATE TABLE IF NOT EXISTS image_safety_results(path TEXT PRIMARY KEY,file_size INTEGER NOT NULL,file_modified_at TEXT NOT NULL,model_version TEXT NOT NULL,status TEXT NOT NULL,primary_class TEXT NOT NULL,nsfl_score REAL NOT NULL,nsfw_score REAL NOT NULL,sfw_score REAL NOT NULL,scanned_at TEXT NOT NULL,error TEXT NOT NULL DEFAULT '')""";
            command.ExecuteNonQuery(); _initialized = true; }
    }
    SqliteConnection Open() { var connection = new SqliteConnection(_connectionString); connection.Open(); return connection; }
    static ImageSafetyScanResult Read(SqliteDataReader r) => new(r.GetString(0), Enum.Parse<ImageSafetyScanStatus>(r.GetString(4), true), Enum.Parse<ImageSafetyClass>(r.GetString(5), true), r.GetDouble(6), r.GetDouble(7), r.GetDouble(8), r.GetString(3), DateTime.Parse(r.GetString(9), null, System.Globalization.DateTimeStyles.RoundtripKind), r.GetInt64(1), DateTime.Parse(r.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind), r.GetString(10));
}
