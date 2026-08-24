namespace PrivacyAudit;

/// <summary>
/// Data model for an author promotional project item.
/// </summary>
public sealed class AuthorProject
{
    public string TitleRu { get; init; } = "";
    public string TitleEn { get; init; } = "";
    public string DescriptionRu { get; init; } = "";
    public string DescriptionEn { get; init; } = "";
    public string Url { get; init; } = "";
    public string Icon { get; init; } = "🚀";

    public string GetTitle(string lang) =>
        lang.StartsWith("ru", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(TitleEn)
            ? TitleRu
            : TitleEn;

    public string GetDescription(string lang) =>
        lang.StartsWith("ru", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(DescriptionEn)
            ? DescriptionRu
            : DescriptionEn;
}

/// <summary>
/// Static offline author projects configuration.
/// </summary>
public static class AuthorProjectsConfig
{
    public static readonly AuthorProject[] Projects =
    [
        new()
        {
            TitleRu = "AI360",
            TitleEn = "AI360",
            DescriptionRu = "Умное приложение для генерации 360° сферических фото непосредственно в твоем телефоне.",
            DescriptionEn = "Smart app for generating 360° spherical photos directly on your phone.",
            Url = "http://tnick.cc/ai360/",
            Icon = "🌐"
        },
        new()
        {
            TitleRu = "Файлекса ТГ-бот",
            TitleEn = "Filexa TG Bot",
            DescriptionRu = "🎨 Генерация изображений и видео – идея твоя, исполнение мое.",
            DescriptionEn = "🎨 AI image & video generation – your idea, my execution.",
            Url = "https://t.me/FilexaAIBot",
            Icon = "🎨"
        },
        new()
        {
            TitleRu = "tnick.cc",
            TitleEn = "tnick.cc",
            DescriptionRu = "Персональный сайт разработчика, проекты, статьи и исследования.",
            DescriptionEn = "Developer's personal website, projects, articles, and research.",
            Url = "https://tnick.cc",
            Icon = "💻"
        },
        new()
        {
            TitleRu = "NotBad Video Wallpaper",
            TitleEn = "NotBad Video Wallpaper",
            DescriptionRu = "Легковесное приложение для живых видеообоев на рабочий стол Windows без лишней нагрузки на систему.",
            DescriptionEn = "Lightweight Windows application for setting live video wallpapers on desktop without system overhead.",
            Url = "https://github.com/Teutonick/NotBad-Video-Wallpaper",
            Icon = "🎬"
        }
    ];
}
