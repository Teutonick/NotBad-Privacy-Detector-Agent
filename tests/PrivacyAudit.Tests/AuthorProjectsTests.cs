using Xunit;

namespace PrivacyAudit.Tests;

public sealed class AuthorProjectsTests
{
    [Fact]
    public void AuthorProjectsConfig_ContainsConfiguredProjects()
    {
        var projects = AuthorProjectsConfig.Projects;
        Assert.NotNull(projects);
        Assert.Equal(4, projects.Length);

        // Project 1: AI360
        var p1 = projects[0];
        Assert.Equal("AI360", p1.TitleRu);
        Assert.Equal("AI360", p1.TitleEn);
        Assert.Equal("http://tnick.cc/ai360/", p1.Url);
        Assert.Equal("🌐", p1.Icon);
        Assert.Contains("360°", p1.DescriptionRu);

        // Project 2: Filexa TG Bot
        var p2 = projects[1];
        Assert.Equal("Файлекса ТГ-бот", p2.TitleRu);
        Assert.Equal("Filexa TG Bot", p2.TitleEn);
        Assert.Equal("https://t.me/FilexaAIBot", p2.Url);
        Assert.Equal("🎨", p2.Icon);
        Assert.Contains("Генерация изображений", p2.DescriptionRu);

        // Project 3: tnick.cc
        var p3 = projects[2];
        Assert.Equal("tnick.cc", p3.TitleRu);
        Assert.Equal("tnick.cc", p3.TitleEn);
        Assert.Equal("https://tnick.cc", p3.Url);
        Assert.Equal("💻", p3.Icon);

        // Project 4: NotBad Video Wallpaper
        var p4 = projects[3];
        Assert.Equal("NotBad Video Wallpaper", p4.TitleRu);
        Assert.Equal("NotBad Video Wallpaper", p4.TitleEn);
        Assert.Equal("https://github.com/Teutonick/NotBad-Video-Wallpaper", p4.Url);
        Assert.Equal("🎬", p4.Icon);
    }

    [Theory]
    [InlineData("ru", "AI360", "Файлекса ТГ-бот", "tnick.cc", "NotBad Video Wallpaper")]
    [InlineData("en", "AI360", "Filexa TG Bot", "tnick.cc", "NotBad Video Wallpaper")]
    public void AuthorProject_GetTitle_RespectsLanguage(string lang, string expected1, string expected2, string expected3, string expected4)
    {
        Assert.Equal(expected1, AuthorProjectsConfig.Projects[0].GetTitle(lang));
        Assert.Equal(expected2, AuthorProjectsConfig.Projects[1].GetTitle(lang));
        Assert.Equal(expected3, AuthorProjectsConfig.Projects[2].GetTitle(lang));
        Assert.Equal(expected4, AuthorProjectsConfig.Projects[3].GetTitle(lang));
    }

    [Theory]
    [InlineData("Docs/DISCLAIMER.md")]
    [InlineData("Docs/PRIVACY.md")]
    [InlineData("Docs/THIRD_PARTY_NOTICES.md")]
    public void DocumentViewer_CanLoadEmbeddedDocs(string resourcePath)
    {
        var text = DocumentViewerWindow.LoadEmbeddedText(resourcePath);
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.True(text.Length > 50);
    }
}
