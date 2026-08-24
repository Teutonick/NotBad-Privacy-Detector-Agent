using Microsoft.Data.Sqlite;
using System.Text.Json;
using PrivacyAudit.Core;

namespace PrivacyAudit.Storage;

public sealed class AuditDatabase
{
    readonly string _path;
    // A second PrivacyAudit instance must not wait indefinitely on the local database.
    string Cs => $"Data Source={_path};Default Timeout=5";
    public AuditDatabase(string path) { _path = path; Directory.CreateDirectory(Path.GetDirectoryName(path)!); Initialize(); }
    void Initialize()
    {
        using var c = new SqliteConnection(Cs); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = """
        CREATE TABLE IF NOT EXISTS scans(id TEXT PRIMARY KEY, started_at TEXT, finished_at TEXT, computer_name TEXT, windows_version TEXT, app_version TEXT);
        CREATE TABLE IF NOT EXISTS findings(id TEXT PRIMARY KEY, scan_id TEXT, scanner_id TEXT, category TEXT, subcategory TEXT, path TEXT, display_name TEXT, size_bytes INTEGER, created_at TEXT, modified_at TEXT, last_access_at TEXT, exposure_score INTEGER, reasons TEXT, age_class TEXT, ignored INTEGER DEFAULT 0);
        CREATE TABLE IF NOT EXISTS exclusions(path TEXT PRIMARY KEY);
        CREATE TABLE IF NOT EXISTS ml_feedback(finding_id TEXT NOT NULL, path_key TEXT PRIMARY KEY, label INTEGER NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, feature_schema_version INTEGER NOT NULL, feature_json TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS file_provenance(finding_id TEXT PRIMARY KEY, path TEXT NOT NULL, file_size INTEGER NOT NULL, file_modified_at TEXT, analysis_version INTEGER NOT NULL, analyzed_at TEXT NOT NULL, result_json TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS provenance_evidence(provenance_id TEXT NOT NULL, evidence_type TEXT NOT NULL, description TEXT NOT NULL, weight INTEGER NOT NULL, source TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_findings_scan ON findings(scan_id);
        CREATE INDEX IF NOT EXISTS ix_ml_feedback_schema ON ml_feedback(feature_schema_version);
        """; cmd.ExecuteNonQuery();
    }
    public IReadOnlyList<string> GetExclusions() { var x = new List<string>(); using var c = new SqliteConnection(Cs); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT path FROM exclusions"; using var r = cmd.ExecuteReader(); while (r.Read()) x.Add(r.GetString(0)); return x; }
    public void AddExclusion(string path) { using var c = new SqliteConnection(Cs); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "INSERT OR IGNORE INTO exclusions(path) VALUES($p)"; cmd.Parameters.AddWithValue("$p", path); cmd.ExecuteNonQuery(); }
    public void SetPersonalFeedback(Finding finding, bool? label)
    {
        var key = PersonalAttentionFeatureExtractor.PathKey(finding.Path);
        SetPersonalFeedback(finding.Id.ToString(), key, PersonalAttentionFeatureExtractor.Extract(finding, label ?? false), label);
    }
    public void SetPersonalFeedback(string itemId, string feedbackKey, PersonalAttentionFeatures features, bool? label)
    {
        using var c = new SqliteConnection(Cs); c.Open(); using var cmd = c.CreateCommand();
        if (label is null) { cmd.CommandText = "DELETE FROM ml_feedback WHERE path_key=$path"; cmd.Parameters.AddWithValue("$path", feedbackKey); cmd.ExecuteNonQuery(); return; }
        features.Label = label.Value;
        var now = DateTime.UtcNow.ToString("O");
        cmd.CommandText = """
        INSERT INTO ml_feedback(finding_id,path_key,label,created_at,updated_at,feature_schema_version,feature_json)
        VALUES($id,$path,$label,$now,$now,$schema,$features)
        ON CONFLICT(path_key) DO UPDATE SET finding_id=excluded.finding_id,label=excluded.label,updated_at=excluded.updated_at,feature_schema_version=excluded.feature_schema_version,feature_json=excluded.feature_json
        """;
        cmd.Parameters.AddWithValue("$id", itemId); cmd.Parameters.AddWithValue("$path", feedbackKey);
        cmd.Parameters.AddWithValue("$label", label.Value ? 1 : 0); cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$schema", PersonalAttentionSchema.Version);
        cmd.Parameters.AddWithValue("$features", PersonalAttentionFeatureExtractor.Serialize(features));
        cmd.ExecuteNonQuery();
        PrunePersonalFeedback(c);
    }
    public IReadOnlyList<PersonalFeedbackRecord> GetPersonalFeedback(int? featureSchemaVersion = null)
    {
        var values = new List<PersonalFeedbackRecord>(); using var c = new SqliteConnection(Cs); c.Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT finding_id,path_key,label,created_at,updated_at,feature_schema_version,feature_json FROM ml_feedback" + (featureSchemaVersion is null ? "" : " WHERE feature_schema_version=$schema");
        if (featureSchemaVersion is int schema) cmd.Parameters.AddWithValue("$schema", schema);
        using var r = cmd.ExecuteReader(); while (r.Read()) values.Add(new(r.GetString(0), r.GetString(1), r.GetInt32(2) != 0, DateTime.Parse(r.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind), DateTime.Parse(r.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind), r.GetInt32(5), r.GetString(6)));
        return values;
    }
    public PersonalModelStats GetPersonalModelStats(int trainedSamples = 0) { var all = GetPersonalFeedback(PersonalAttentionSchema.Version); var positive = all.Count(x => x.Label); return new(all.Count, positive, all.Count - positive, trainedSamples); }
    void PrunePersonalFeedback(SqliteConnection c)
    {
        using var prune = c.CreateCommand();
        prune.CommandText = "DELETE FROM ml_feedback WHERE path_key IN (SELECT path_key FROM ml_feedback ORDER BY updated_at ASC LIMIT -1 OFFSET $keep)";
        prune.Parameters.AddWithValue("$keep", PersonalAttentionSchema.MaxFeedbackRows);
        prune.ExecuteNonQuery();
    }
    public void DeletePersonalFeedback() { using var c = new SqliteConnection(Cs); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "DELETE FROM ml_feedback"; cmd.ExecuteNonQuery(); }
    public FileProvenanceResult? GetProvenance(Finding finding)
    {
        using var c = new SqliteConnection(Cs); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT result_json FROM file_provenance WHERE finding_id=$id AND path=$path";
        cmd.Parameters.AddWithValue("$id", finding.Id.ToString()); cmd.Parameters.AddWithValue("$path", finding.Path);
        var json = cmd.ExecuteScalar() as string; if (string.IsNullOrWhiteSpace(json)) return null; try { return JsonSerializer.Deserialize<FileProvenanceResult>(json); } catch { return null; }
    }
    public void SaveProvenance(FileProvenanceResult result)
    {
        using var c = new SqliteConnection(Cs); c.Open(); using var tx = c.BeginTransaction();
        using (var cmd = c.CreateCommand()) { cmd.Transaction = tx; cmd.CommandText = "INSERT INTO file_provenance(finding_id,path,file_size,file_modified_at,analysis_version,analyzed_at,result_json) VALUES($id,$path,$size,$modified,$version,$at,$json) ON CONFLICT(finding_id) DO UPDATE SET path=excluded.path,file_size=excluded.file_size,file_modified_at=excluded.file_modified_at,analysis_version=excluded.analysis_version,analyzed_at=excluded.analyzed_at,result_json=excluded.result_json"; cmd.Parameters.AddWithValue("$id", result.FindingId.ToString()); cmd.Parameters.AddWithValue("$path", result.Path); cmd.Parameters.AddWithValue("$size", result.FileSize); cmd.Parameters.AddWithValue("$modified", result.FileModifiedAt?.ToString("O") ?? ""); cmd.Parameters.AddWithValue("$version", result.EngineVersion); cmd.Parameters.AddWithValue("$at", result.AnalyzedAt.ToString("O")); cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(result)); cmd.ExecuteNonQuery(); }
        using (var cmd = c.CreateCommand()) { cmd.Transaction = tx; cmd.CommandText = "DELETE FROM provenance_evidence WHERE provenance_id=$id;"; cmd.Parameters.AddWithValue("$id", result.FindingId.ToString()); cmd.ExecuteNonQuery(); foreach (var e in result.Evidence) { using var q = c.CreateCommand(); q.Transaction = tx; q.CommandText = "INSERT INTO provenance_evidence(provenance_id,evidence_type,description,weight,source) VALUES($id,$type,$description,$weight,$source)"; q.Parameters.AddWithValue("$id", result.FindingId.ToString()); q.Parameters.AddWithValue("$type", e.EvidenceType); q.Parameters.AddWithValue("$description", e.Description); q.Parameters.AddWithValue("$weight", e.Weight); q.Parameters.AddWithValue("$source", e.Source); q.ExecuteNonQuery(); } }
        tx.Commit();
    }
    public void DeleteProvenance() { using var c = new SqliteConnection(Cs); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "DELETE FROM provenance_evidence; DELETE FROM file_provenance;"; cmd.ExecuteNonQuery(); }
    public void PruneAuditHistory(DateTime cutoffUtc)
    {
        var cutoff = cutoffUtc.ToString("O");
        using var c = new SqliteConnection(Cs); c.Open(); using var tx = c.BeginTransaction();
        using (var cmd = c.CreateCommand()) { cmd.Transaction = tx; cmd.CommandText = "DELETE FROM findings WHERE scan_id IN (SELECT id FROM scans WHERE finished_at < $cutoff); DELETE FROM scans WHERE finished_at < $cutoff; DELETE FROM file_provenance WHERE analyzed_at < $cutoff; DELETE FROM provenance_evidence WHERE provenance_id NOT IN (SELECT finding_id FROM file_provenance);"; cmd.Parameters.AddWithValue("$cutoff", cutoff); cmd.ExecuteNonQuery(); }
        tx.Commit();
        // Free pages are reused by SQLite; VACUUM is intentionally left to the explicit cleanup action
        // so startup retention never blocks the UI on a large audit database.
    }
    public void Save(Guid scanId, DateTime started, IEnumerable<Finding> findings)
    {
        using var c = new SqliteConnection(Cs); c.Open(); using var tx = c.BeginTransaction();
        using (var s = c.CreateCommand()) { s.Transaction = tx; s.CommandText = "INSERT INTO scans VALUES($id,$s,$f,$pc,$os,$v)"; s.Parameters.AddWithValue("$id", scanId.ToString()); s.Parameters.AddWithValue("$s", started.ToString("O")); s.Parameters.AddWithValue("$f", DateTime.UtcNow.ToString("O")); s.Parameters.AddWithValue("$pc", Environment.MachineName); s.Parameters.AddWithValue("$os", Environment.OSVersion.ToString()); s.Parameters.AddWithValue("$v", "1.0.0"); s.ExecuteNonQuery(); }
        foreach (var f in findings) { using var q = c.CreateCommand(); q.Transaction = tx; q.CommandText = "INSERT INTO findings VALUES($id,$scan,$scanner,$cat,$sub,$path,$name,$size,$created,$modified,$access,$score,$reasons,$age,$ignored)"; q.Parameters.AddWithValue("$id", f.Id.ToString()); q.Parameters.AddWithValue("$scan", scanId.ToString()); q.Parameters.AddWithValue("$scanner", f.ScannerId); q.Parameters.AddWithValue("$cat", f.Category); q.Parameters.AddWithValue("$sub", f.Subcategory); q.Parameters.AddWithValue("$path", f.Path); q.Parameters.AddWithValue("$name", f.DisplayName); q.Parameters.AddWithValue("$size", f.SizeBytes); q.Parameters.AddWithValue("$created", f.CreatedAt?.ToString("O") ?? ""); q.Parameters.AddWithValue("$modified", f.ModifiedAt?.ToString("O") ?? ""); q.Parameters.AddWithValue("$access", f.LastAccessAt?.ToString("O") ?? ""); q.Parameters.AddWithValue("$score", f.ExposureScore); q.Parameters.AddWithValue("$reasons", f.ReasonDisplay); q.Parameters.AddWithValue("$age", f.AgeClass); q.Parameters.AddWithValue("$ignored", f.Ignored ? 1 : 0); q.ExecuteNonQuery(); }
        tx.Commit();
    }
    public void DeleteDatabase() { using var c = new SqliteConnection(Cs); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "DELETE FROM findings; DELETE FROM scans; DELETE FROM exclusions;"; cmd.ExecuteNonQuery(); }
}
