using PrivacyAudit.Core;

namespace PrivacyAudit.Tests;

public sealed class SecretDetectorTests
{
    [Fact]
    public void SecretScan_DetectsOpenAiAndGithubKeys()
    {
        var text = @"
OPENAI_API_KEY=sk-proj-abc123456789012345678901234567890
GITHUB_TOKEN=ghp_123456789012345678901234567890123456
DATABASE_URL=postgres://user:superpass123@localhost:5432/maindb
";

        var result = SecretDetector.Scan(text);

        Assert.True(result.TotalMatches >= 3);
        Assert.Contains("OpenAI_Key", result.Categories);
        Assert.Contains("GitHub_Token", result.Categories);
        Assert.Contains("DatabaseConnection", result.Categories);
    }

    [Fact]
    public void SecretScan_DetectsPrivateKeysAndJwt()
    {
        var text = @"
-----BEGIN RSA PRIVATE KEY-----
MIIEowIBAAKCAQEA0Y1+
-----END RSA PRIVATE KEY-----
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.doNotRealKeySignatureHere
";

        var result = SecretDetector.Scan(text);

        Assert.Contains("PrivateKey", result.Categories);
        Assert.Contains("JWT_Token", result.Categories);
        Assert.Contains("Bearer_Token", result.Categories);
    }

    [Fact]
    public void CalculateEntropy_CalculatesHighEntropyForRandomStrings()
    {
        var repeated = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var random = "aB3$k9#zL1@mP8*qW5&vR2^yX7!tN4%c";

        var lowEntropy = SecretDetector.CalculateEntropy(repeated);
        var highEntropy = SecretDetector.CalculateEntropy(random);

        Assert.Equal(0.0, lowEntropy);
        Assert.True(highEntropy >= 4.5);
    }

    [Fact]
    public void SecretScan_DoesNotReportUnicodePunctuationAsHighEntropySecret()
    {
        var decorativeSymbols = ")] }༻༽᚜⁆⁾₎⌉⌋〉❩❫❭❯❱❳❵⟆⟧⟩⟫⟭⟯⦄⦆⦈⦊⦌⦎⦐⦒⦔⦖⦘⧙⧛⧽⸣⸥⸧⸩〉》」』】〕〗〙〛〞〟﴾︘︶︸︺︼︾﹀﹂﹄﹈﹚﹜﹞）］｝｠｣";

        var result = SecretDetector.Scan(decorativeSymbols);

        Assert.DoesNotContain(result.Matches, x => x.Category == "HighEntropy_Secret");
    }

    [Theory]
    [InlineData("a secret 17th_century tradition described in an ordinary dictionary entry")]
    [InlineData("【ゲーミングチェア 】快適すぎるんですけど!!GTRACINGどうよ★")]
    [InlineData("writer_config: CheckpointWriterConfig = field(default_factory=CheckpointWriterConfig)")]
    [InlineData(".. _github_comfyui-nunchaku_example_workflows: https://github.com/nunchaku-tech/ComfyUI-nunchaku/tree/main/example_workflows")]
    public void SecretScan_DoesNotReportProseUnicodeOrCodeIdentifiers(string text)
    {
        var result = SecretDetector.Scan(text);

        Assert.Empty(result.Matches);
    }

    [Fact]
    public void SecretScan_DetectsOpaqueMixedAsciiTokenByEntropy()
    {
        var result = SecretDetector.Scan("Q7mN2vR9xL4kP8sT5wY1cD6hF3jB0zUa");

        Assert.Contains(result.Matches, x => x.Category == "HighEntropy_Secret");
    }

    [Fact]
    public void SecretScan_DetectsExplicitNamedSecretAssignment()
    {
        var result = SecretDetector.Scan("client_secret: Q7mN2vR9xL4kP8sT5wY1cD6hF3jB0zUa");

        Assert.Contains(result.Matches, x => x.Category == "ApiKey");
    }
}
