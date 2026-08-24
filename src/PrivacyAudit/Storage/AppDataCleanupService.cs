using Microsoft.Data.Sqlite;

namespace PrivacyAudit.Storage;

public sealed record CleanupResult(IReadOnlyList<string> RemovedItems);

/// <summary>Deletes only data owned by this application and never follows reparse points.</summary>
public sealed class AppDataCleanupService
{
    public const string AppDataFolderName = "NotBadPrivacyDetectorAgent";
    readonly string _root;

    public AppDataCleanupService(string root, string? allowedBaseDirectory = null)
    {
        var allowedBase = Path.GetFullPath(allowedBaseDirectory ??
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var expected = Path.GetFullPath(Path.Combine(allowedBase, AppDataFolderName));
        _root = Path.GetFullPath(root);
        if (!string.Equals(_root.TrimEnd(Path.DirectorySeparatorChar), expected.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Cleanup root is outside the application's owned local-data directory.", nameof(root));
    }

    public CleanupResult ClearCachesAndAuditResults()
    {
        var removed = new List<string>();
        var database = Path.Combine(_root, "privacy-audit.db");
        if (File.Exists(database))
        {
            SqliteConnection.ClearAllPools();
            using var connection = new SqliteConnection($"Data Source={database};Default Timeout=5");
            connection.Open();
            foreach (var table in new[] { "findings", "scans", "exclusions", "people_scan_results" })
            {
                using var exists = connection.CreateCommand();
                exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
                exists.Parameters.AddWithValue("$name", table);
                if (Convert.ToInt32(exists.ExecuteScalar()) == 0) continue;
                using var clear = connection.CreateCommand();
                clear.CommandText = $"DELETE FROM \"{table}\"";
                clear.ExecuteNonQuery();
            }
            using var vacuum = connection.CreateCommand();
            vacuum.CommandText = "VACUUM";
            vacuum.ExecuteNonQuery();
            removed.Add("privacy-audit.db: audit results and caches");
        }

        DeleteFile(Path.Combine(_root, "last-scan.json"), removed);
        DeleteFile(Path.Combine(_root, "crash.log"), removed);
        DeleteFile(Path.Combine(_root, "people-model.log"), removed);
        DeleteDirectory(Path.Combine(_root, "Models", "YuNet"), removed);
        RemoveIfEmpty(Path.Combine(_root, "Models"));
        return new(removed);
    }

    public CleanupResult DeleteAllApplicationData()
    {
        SqliteConnection.ClearAllPools();
        if (!Directory.Exists(_root)) return new([]);
        SafeDeleteTree(_root, _root);
        return new([_root]);
    }

    static void DeleteFile(string path, List<string> removed)
    {
        if (!File.Exists(path)) return;
        File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
        removed.Add(Path.GetFileName(path));
    }

    static void DeleteDirectory(string path, List<string> removed)
    {
        if (!Directory.Exists(path)) return;
        SafeDeleteTree(path, path);
        removed.Add(Path.GetFileName(path));
    }

    static void RemoveIfEmpty(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            Directory.Delete(path);
    }

    static void SafeDeleteTree(string path, string root)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Refusing to delete outside application data: {fullPath}");

        var attributes = File.GetAttributes(fullPath);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(fullPath, false);
            return;
        }

        foreach (var file in Directory.EnumerateFiles(fullPath))
        {
            var resolved = Path.GetFullPath(file);
            if (!resolved.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Refusing to delete outside application data: {resolved}");
            File.SetAttributes(resolved, FileAttributes.Normal);
            File.Delete(resolved);
        }
        foreach (var directory in Directory.EnumerateDirectories(fullPath))
            SafeDeleteTree(directory, root);
        Directory.Delete(fullPath);
    }
}
