namespace PrivacyAudit.Core;

public static class NsfwMediaScope
{
    public static bool Matches(string category, bool includeImages, bool includeVideos) =>
        (includeImages && category == "Images") || (includeVideos && category == "Video");
}
