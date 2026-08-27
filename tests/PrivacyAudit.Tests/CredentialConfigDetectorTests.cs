using PrivacyAudit.Core;

namespace PrivacyAudit.Tests;

public sealed class CredentialConfigDetectorTests
{
    [Fact]
    public void EnvironmentBackedPythonConfigIsNotReportedAsExposedCredentials()
    {
        const string source = """
            import os
            token_pepper = os.getenv("TOKEN_PEPPER")
            filexa_client_token = os.environ.get("FILEXA_CLIENT_TOKEN")
            ai360_client_token = os.getenv("AI360_CLIENT_TOKEN", "")
            waf_authenticated_requests_per_minute = 60
            has_password = False
            endpoint = "https://api.telegram.org"
            """;

        var result = CredentialConfigDetector.Analyze("config.py", source);

        Assert.False(result.IsCredentialConfig);
        Assert.Empty(result.ExposedParameters);
    }

    [Fact]
    public void HardCodedCredentialValueRemainsVisible()
    {
        var result = CredentialConfigDetector.Analyze("config.py", "telegram_bot_token = \"123456:hard-coded-secret\"\n");

        Assert.True(result.IsCredentialConfig);
        Assert.Contains("telegram_bot_token", result.ExposedParameters);
    }

    [Fact]
    public void NonEmptyEnvironmentFallbackRemainsVisible()
    {
        var result = CredentialConfigDetector.Analyze("config.py", "token = os.getenv(\"TOKEN\", \"fallback-secret\")\n");

        Assert.True(result.IsCredentialConfig);
        Assert.Contains("token", result.ExposedParameters);
    }

    [Theory]
    [InlineData("clientSecret")]
    [InlineData("accessToken")]
    [InlineData("dbPassword")]
    [InlineData("privateKey")]
    [InlineData("signingKey")]
    [InlineData("authHeader")]
    public void CamelCaseCredentialNamesRemainVisible(string key)
    {
        var result = CredentialConfigDetector.Analyze("config.py", $"{key} = \"hard-coded-value-123\"\n");

        Assert.True(result.IsCredentialConfig);
        Assert.Contains(key, result.ExposedParameters);
    }

    [Fact]
    public void EnvironmentSetDefaultReportsConcreteFallback()
    {
        var result = CredentialConfigDetector.Analyze("config.py", "token = os.environ.setdefault(\"TOKEN\", \"hard-coded-fallback\")\n");

        Assert.True(result.IsCredentialConfig);
        Assert.Contains("token", result.ExposedParameters);
    }

    [Fact]
    public void DollarPrefixedHardCodedValueIsNotMistakenForEnvironmentName()
    {
        var result = CredentialConfigDetector.Analyze("config.py", "password = \"$uperSecret123\"\n");

        Assert.True(result.IsCredentialConfig);
        Assert.Contains("password", result.ExposedParameters);
    }

    [Theory]
    [InlineData("$TOKEN")]
    [InlineData("$token")]
    [InlineData("${TOKEN}")]
    [InlineData("%TOKEN%")]
    [InlineData("{{ TOKEN }}")]
    [InlineData("<TOKEN>")]
    public void CompleteEnvironmentPlaceholdersAreNotExposedValues(string placeholder)
    {
        var result = CredentialConfigDetector.Analyze("config.py", $"token = \"{placeholder}\"\n");

        Assert.False(result.IsCredentialConfig);
    }
}
