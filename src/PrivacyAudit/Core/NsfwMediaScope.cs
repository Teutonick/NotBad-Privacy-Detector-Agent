namespace PrivacyAudit.Core;

public static class MediaScope
{
    public static bool Matches(string category, bool includeImages, bool includeVideos) =>
        (includeImages && category == "Images") || (includeVideos && category == "Video");
}

public static class MediaCategoryFilter
{
    public static bool UsesTypeScope(string? category) => category is "AllMedia" or "People" or "NSFW" or "NoPeople" or "Errors" or "Unscanned";
}

public static class NsfwMediaScope
{
    public static bool Matches(string category, bool includeImages, bool includeVideos) => MediaScope.Matches(category, includeImages, includeVideos);
}
