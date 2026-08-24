using System.IO.Compression;
using System.Text;
using PrivacyAudit.Core;
using Xunit;

namespace PrivacyAudit.Tests;

public sealed class DeepDetectorTests
{
    [Fact]
    public void CredentialConfigDetector_DetectsEnvFileWithDatabaseAndAuthKeys()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cfg_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var envFile = Path.Combine(tempDir, ".env");
            var content = "DB_HOST=127.0.0.1\nDB_PORT=5432\nDB_USER=postgres\nDB_PASSWORD=super_secret_pass\nAWS_SECRET_ACCESS_KEY=AKIAIOSFODNN7EXAMPLE\nAPI_URL=https://api.example.com";
            File.WriteAllText(envFile, content);

            var result = CredentialConfigDetector.Analyze(envFile);

            Assert.True(result.IsCredentialConfig);
            Assert.Contains("Environment", result.ConfigType);
            Assert.Contains("DB_PASSWORD", result.ExposedParameters);
            Assert.Contains("AWS_SECRET_ACCESS_KEY", result.ExposedParameters);
            Assert.Contains("https://api.example.com", result.Endpoints);
            Assert.Equal("High", result.ExposureLevel);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CredentialConfigDetector_DetectsNpmrcAndKubeconfig()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"npm_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var npmFile = Path.Combine(tempDir, ".npmrc");
            File.WriteAllText(npmFile, "//registry.npmjs.org/:_authToken=npm_1234567890abcdef");

            var result = CredentialConfigDetector.Analyze(npmFile);

            Assert.True(result.IsCredentialConfig);
            Assert.Contains("NPM", result.ConfigType);
            Assert.Equal("High", result.ExposureLevel);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void IdentityTraceDetector_FindsMentionsOfUserAndGitEmail()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"idt_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var profile = new UserIdentityProfile();
            profile.TermsToCategory["alice_dev"] = "Windows Account";
            profile.TermsToCategory["alice@company.org"] = "Git Email";
            profile.TermsToCategory["WORKSTATION-99"] = "PC Hostname";

            var docFile = Path.Combine(tempDir, "project_notes.txt");
            var content = "Created by alice_dev on machine WORKSTATION-99.\nContact: alice@company.org for access.\nReview complete by alice_dev.";
            File.WriteAllText(docFile, content);

            var result = IdentityTraceDetector.Analyze(docFile, profile);

            Assert.True(result.HasIdentityTrace);
            Assert.True(result.TotalMentions >= 4);
            Assert.True(result.MatchedTerms.ContainsKey("alice_dev"));
            Assert.True(result.MatchedTerms.ContainsKey("alice@company.org"));
            Assert.True(result.MatchedTerms.ContainsKey("WORKSTATION-99"));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ArchiveInspector_InspectsZipContentsInMemoryWithoutDiskExtraction()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"arch_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var zipPath = Path.Combine(tempDir, "backup_archive.zip");

            using (var fileStream = new FileStream(zipPath, FileMode.Create))
            using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
            {
                // 1. Sensitive: passport scan
                var e1 = archive.CreateEntry("docs/passport_scan.jpg");
                using (var writer = new StreamWriter(e1.Open())) writer.Write("dummy image data");

                // 2. Sensitive: passwords
                var e2 = archive.CreateEntry("secrets/passwords.txt");
                using (var writer = new StreamWriter(e2.Open())) writer.Write("admin:password123");

                // 3. Sensitive: .env
                var e3 = archive.CreateEntry("app/.env");
                using (var writer = new StreamWriter(e3.Open())) writer.Write("SECRET=123");

                // 4. Non-sensitive: readme
                var e4 = archive.CreateEntry("README.md");
                using (var writer = new StreamWriter(e4.Open())) writer.Write("# Hello world");
            }

            var result = ArchiveInspector.Inspect(zipPath);

            Assert.True(result.IsArchive);
            Assert.Equal(4, result.TotalEntries);
            Assert.Equal(3, result.SensitiveEntriesCount);
            Assert.Equal("Critical", result.PrivacyScore);
            Assert.Contains("passport_scan.jpg", result.TreeView);
            Assert.Contains("passwords.txt", result.TreeView);
            Assert.Contains(".env", result.TreeView);
            Assert.Contains("├─", result.TreeView);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
