namespace PrivacyAudit.Core;

public static class FindingFilter
{
    public static readonly long[] SizeThresholds =
    [
        0L,
        100L * 1024,
        500L * 1024,
        1L * 1024 * 1024,
        5L * 1024 * 1024,
        20L * 1024 * 1024,
        100L * 1024 * 1024,
        500L * 1024 * 1024,
        1L * 1024 * 1024 * 1024,
        5L * 1024 * 1024 * 1024
    ];

    public static readonly string[] SizeKeys =
    [
        "AnySize",
        "SizeGte100KB",
        "SizeGte500KB",
        "SizeGte1MB",
        "SizeGte5MB",
        "SizeGte20MB",
        "SizeGte100MB",
        "SizeGte500MB",
        "SizeGte1GB",
        "SizeGte5GB"
    ];

    public static readonly string[] AgeKeys =
    [
        "AnyAge",
        "AgeRecent1Month",
        "AgeRecent6Months",
        "AgeRecent1Year",
        "AgeOlder6Months",
        "AgeOlder1Year",
        "AgeOlder2Years",
        "AgeOlder3Years",
        "AgeOlder5Years"
    ];

    public static bool MatchesSize(Finding finding, int sizeStep)
    {
        if (sizeStep <= 0 || sizeStep >= SizeThresholds.Length) return true;
        return finding.SizeBytes >= SizeThresholds[sizeStep];
    }

    public static bool MatchesAge(Finding finding, int ageStep, DateTime? referenceTime = null)
    {
        if (ageStep <= 0) return true;
        var date = finding.ModifiedAt ?? finding.CreatedAt;
        if (date is null) return false;
        var now = referenceTime ?? DateTime.Now;
        var ageDays = (now - date.Value).TotalDays;
        if (ageDays < 0) ageDays = 0;

        return ageStep switch
        {
            1 => ageDays <= 30.5,
            2 => ageDays <= 182.5,
            3 => ageDays <= 365.25,
            4 => ageDays > 182.5,
            5 => ageDays > 365.25,
            6 => ageDays > 730.5,
            7 => ageDays > 1095.75,
            8 => ageDays > 1826.25,
            _ => true
        };
    }

    public static string GetSizeKey(int step) => step >= 0 && step < SizeKeys.Length ? SizeKeys[step] : SizeKeys[0];
    public static string GetAgeKey(int step) => step >= 0 && step < AgeKeys.Length ? AgeKeys[step] : AgeKeys[0];
}
