using System.Globalization;

namespace PrivacyAudit.Storage;

public static class AuditIdentity
{
    public static string Create(AuditSnapshotContext? context, DateTime fallbackSavedAtUtc)
    {
        if (context is not null)
            return $"v2:{context.StartedAtUtc.Ticks.ToString(CultureInfo.InvariantCulture)}:{context.CompletedAtUtc.Ticks.ToString(CultureInfo.InvariantCulture)}";
        return $"v2:snapshot:{fallbackSavedAtUtc.Ticks.ToString(CultureInfo.InvariantCulture)}";
    }

    public static bool Matches(string? candidate, string current, AuditSnapshotContext? context)
    {
        if (string.Equals(candidate, current, StringComparison.Ordinal)) return true;
        if (string.IsNullOrWhiteSpace(candidate) || context is null) return false;

        // Version 1 fingerprints ended with the mutable finding count. Accept them by
        // their immutable audit timestamps once, then callers migrate them to v2.
        var parts = candidate.Split(':');
        return parts.Length == 3
            && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var started)
            && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var completed)
            && started == context.StartedAtUtc.Ticks
            && completed == context.CompletedAtUtc.Ticks;
    }
}
