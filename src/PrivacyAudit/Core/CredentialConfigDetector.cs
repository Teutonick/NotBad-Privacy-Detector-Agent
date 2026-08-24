using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PrivacyAudit.Core;

public sealed class CredentialConfigResult
{
    [JsonPropertyName("is_credential_config")] public bool IsCredentialConfig { get; set; }
    [JsonPropertyName("config_type")] public string ConfigType { get; set; } = "";
    [JsonPropertyName("exposed_parameters")] public List<string> ExposedParameters { get; set; } = [];
    [JsonPropertyName("endpoints")] public List<string> Endpoints { get; set; } = [];
    [JsonPropertyName("exposure_level")] public string ExposureLevel { get; set; } = "Medium";
    [JsonPropertyName("description")] public string Description { get; set; } = "";

    public static string Serialize(CredentialConfigResult result) => JsonSerializer.Serialize(result);

    public static bool TryParse(string? json, out CredentialConfigResult? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("credential_config", out var prop)) return false;
            result = JsonSerializer.Deserialize<CredentialConfigResult>(prop.GetRawText());
            return result is not null;
        }
        catch
        {
            return false;
        }
    }

    public static string InjectIntoMetadata(string currentJson, CredentialConfigResult result)
    {
        try
        {
            var dict = string.IsNullOrWhiteSpace(currentJson)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(currentJson) ?? new();
            dict["credential_config"] = result;
            return JsonSerializer.Serialize(dict);
        }
        catch
        {
            return JsonSerializer.Serialize(new { credential_config = result });
        }
    }
}

public static class CredentialConfigDetector
{
    static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(400);

    public static CredentialConfigResult Analyze(string filePath, string? textContent = null)
    {
        var result = new CredentialConfigResult();
        if (string.IsNullOrWhiteSpace(filePath)) return result;

        var fileName = Path.GetFileName(filePath);
        var dirName = Path.GetDirectoryName(filePath) ?? "";
        var ext = Path.GetExtension(filePath);

        // 1. Check by filename & path signatures
        if (fileName.StartsWith(".env", StringComparison.OrdinalIgnoreCase))
        {
            result.IsCredentialConfig = true;
            result.ConfigType = "Environment Variables (.env)";
            result.ExposureLevel = fileName.Equals(".env.example", StringComparison.OrdinalIgnoreCase) ? "Low" : "High";
        }
        else if (fileName.Equals(".npmrc", StringComparison.OrdinalIgnoreCase) ||
                 fileName.Equals(".yarnrc", StringComparison.OrdinalIgnoreCase) ||
                 fileName.Equals(".yarnrc.yml", StringComparison.OrdinalIgnoreCase))
        {
            result.IsCredentialConfig = true;
            result.ConfigType = "NPM / Yarn Package Registry Auth";
            result.ExposureLevel = "High";
        }
        else if (fileName.Equals("pip.conf", StringComparison.OrdinalIgnoreCase) ||
                 fileName.Equals(".pypirc", StringComparison.OrdinalIgnoreCase) ||
                 fileName.Equals("pypirc", StringComparison.OrdinalIgnoreCase))
        {
            result.IsCredentialConfig = true;
            result.ConfigType = "Python PyPI Repository & Index Config";
            result.ExposureLevel = "High";
        }
        else if (fileName.Equals("NuGet.Config", StringComparison.OrdinalIgnoreCase) ||
                 fileName.Equals("nuget.config", StringComparison.OrdinalIgnoreCase))
        {
            result.IsCredentialConfig = true;
            result.ConfigType = "NuGet Package Source Credentials";
            result.ExposureLevel = "Medium";
        }
        else if (fileName.Equals("gradle.properties", StringComparison.OrdinalIgnoreCase))
        {
            result.IsCredentialConfig = true;
            result.ConfigType = "Gradle Signing & Repository Auth";
            result.ExposureLevel = "Medium";
        }
        else if (fileName.Equals("settings.xml", StringComparison.OrdinalIgnoreCase) &&
                 (dirName.Contains(".m2", StringComparison.OrdinalIgnoreCase) || dirName.Contains("maven", StringComparison.OrdinalIgnoreCase)))
        {
            result.IsCredentialConfig = true;
            result.ConfigType = "Maven Server Credentials & Mirrors";
            result.ExposureLevel = "High";
        }
        else if (fileName.StartsWith("docker-compose", StringComparison.OrdinalIgnoreCase) && (ext.Equals(".yml", StringComparison.OrdinalIgnoreCase) || ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase)))
        {
            result.IsCredentialConfig = true;
            result.ConfigType = "Docker Compose Orchestration & Auth";
            result.ExposureLevel = "Medium";
        }
        else if (fileName.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase))
        {
            result.IsCredentialConfig = true;
            result.ConfigType = "Docker Container Build Environment";
            result.ExposureLevel = "Low";
        }
        else if (fileName.Equals("kubeconfig", StringComparison.OrdinalIgnoreCase) ||
                 (fileName.Equals("config", StringComparison.OrdinalIgnoreCase) && dirName.Contains(".kube", StringComparison.OrdinalIgnoreCase)))
        {
            result.IsCredentialConfig = true;
            result.ConfigType = "Kubernetes Cluster & User Tokens (kubeconfig)";
            result.ExposureLevel = "Critical";
        }
        else if ((fileName.Equals("config", StringComparison.OrdinalIgnoreCase) ||
                  fileName.Equals("authorized_keys", StringComparison.OrdinalIgnoreCase) ||
                  fileName.Equals("known_hosts", StringComparison.OrdinalIgnoreCase)) && dirName.Contains(".ssh", StringComparison.OrdinalIgnoreCase))
        {
            result.IsCredentialConfig = true;
            result.ConfigType = "SSH Client Configuration & Keys";
            result.ExposureLevel = "High";
        }
        else if (fileName.Equals(".gitconfig", StringComparison.OrdinalIgnoreCase) ||
                 fileName.Equals(".git-credentials", StringComparison.OrdinalIgnoreCase) ||
                 (fileName.Equals("config", StringComparison.OrdinalIgnoreCase) && dirName.Contains(".git", StringComparison.OrdinalIgnoreCase)))
        {
            result.IsCredentialConfig = true;
            result.ConfigType = "Git Credentials & Author Configuration";
            result.ExposureLevel = fileName.Equals(".git-credentials", StringComparison.OrdinalIgnoreCase) ? "Critical" : "Medium";
        }
        else if (fileName.Equals(".pgpass", StringComparison.OrdinalIgnoreCase) ||
                 fileName.Equals(".my.cnf", StringComparison.OrdinalIgnoreCase) ||
                 fileName.Equals("database.yml", StringComparison.OrdinalIgnoreCase) ||
                 fileName.Equals("connections.json", StringComparison.OrdinalIgnoreCase))
        {
            result.IsCredentialConfig = true;
            result.ConfigType = "Database Connection & Password File";
            result.ExposureLevel = "Critical";
        }
        else if ((fileName.Equals("credentials", StringComparison.OrdinalIgnoreCase) || fileName.Equals("config", StringComparison.OrdinalIgnoreCase)) &&
                 (dirName.Contains(".aws", StringComparison.OrdinalIgnoreCase) ||
                  dirName.Contains(".azure", StringComparison.OrdinalIgnoreCase) ||
                  dirName.Contains(".gcloud", StringComparison.OrdinalIgnoreCase) ||
                  dirName.Contains(".oci", StringComparison.OrdinalIgnoreCase)))
        {
            result.IsCredentialConfig = true;
            result.ConfigType = "Cloud Provider Auth & Profiles (AWS/Azure/GCloud)";
            result.ExposureLevel = "Critical";
        }
        else if (ext.Equals(".ovpn", StringComparison.OrdinalIgnoreCase) ||
                 fileName.EndsWith(".wireguard.conf", StringComparison.OrdinalIgnoreCase) ||
                 (ext.Equals(".conf", StringComparison.OrdinalIgnoreCase) && (fileName.StartsWith("wg", StringComparison.OrdinalIgnoreCase) || fileName.Contains("vpn", StringComparison.OrdinalIgnoreCase))))
        {
            result.IsCredentialConfig = true;
            result.ConfigType = "VPN & WireGuard Private Tunnel Config";
            result.ExposureLevel = "High";
        }
        else if (fileName.EndsWith(".tfvars", StringComparison.OrdinalIgnoreCase) ||
                 fileName.EndsWith(".tfstate", StringComparison.OrdinalIgnoreCase))
        {
            result.IsCredentialConfig = true;
            result.ConfigType = "Terraform State & Infrastructure Variables";
            result.ExposureLevel = "High";
        }

        // 2. Parse text content if provided or file is small enough
        var text = textContent;
        if (text is null && File.Exists(filePath))
        {
            try
            {
                var fi = new FileInfo(filePath);
                if (fi.Length <= 1024 * 1024) // Read up to 1 MB
                {
                    text = TextExtractor.ExtractText(filePath);
                }
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            AnalyzeContent(text, result);
        }

        if (result.IsCredentialConfig)
        {
            result.Description = $"{result.ConfigType} — {result.ExposedParameters.Count} exposed auth parameters, {result.Endpoints.Count} endpoints.";
        }

        return result;
    }

    static void AnalyzeContent(string text, CredentialConfigResult result)
    {
        // Check for specific configuration directives
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines.Take(500))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('#') || line.StartsWith("//") || line.StartsWith(';')) continue;

            // 1. Sensitive Key Names
            try
            {
                var keyMatch = Regex.Match(line, @"^(?:export\s+)?(?<key>[A-Za-z0-9_\-.:]+)\s*(?:=|:)\s*(?<val>.+)$", RegexOptions.None, RegexTimeout);
                if (keyMatch.Success)
                {
                    var key = keyMatch.Groups["key"].Value;
                    if (IsSensitiveKey(key))
                    {
                        result.IsCredentialConfig = true;
                        if (!result.ExposedParameters.Contains(key))
                        {
                            result.ExposedParameters.Add(key);
                        }
                    }
                }
            }
            catch (RegexMatchTimeoutException) { }

            // 2. Endpoints / Hostnames / Registry URLs
            try
            {
                var urlMatch = Regex.Match(line, @"https?://[A-Za-z0-9.-]+(?::[0-9]+)?", RegexOptions.IgnoreCase, RegexTimeout);
                if (urlMatch.Success && !urlMatch.Value.Contains("w3.org") && !urlMatch.Value.Contains("schema.org") && !urlMatch.Value.Contains("schemas.microsoft.com"))
                {
                    if (!result.Endpoints.Contains(urlMatch.Value) && result.Endpoints.Count < 10)
                    {
                        result.Endpoints.Add(urlMatch.Value);
                    }
                }
            }
            catch (RegexMatchTimeoutException) { }

            // 3. Database connection string indicators
            if (line.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("mongodb://", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("postgres://", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("postgresql://", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("mysql://", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("redis://", StringComparison.OrdinalIgnoreCase))
            {
                result.IsCredentialConfig = true;
                if (string.IsNullOrWhiteSpace(result.ConfigType))
                {
                    result.ConfigType = "Database Connection String Config";
                }
                if (!result.ExposedParameters.Contains("Database Connection String"))
                {
                    result.ExposedParameters.Add("Database Connection String");
                }
            }
        }

        // If config type wasn't set by filename but sensitive keys were found
        if (result.IsCredentialConfig && string.IsNullOrWhiteSpace(result.ConfigType))
        {
            result.ConfigType = "Application Configuration & Credentials";
            result.ExposureLevel = result.ExposedParameters.Count >= 3 ? "High" : "Medium";
        }
    }

    static bool IsSensitiveKey(string key)
    {
        return key.Contains("PASS", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("AUTH", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("KEY", StringComparison.OrdinalIgnoreCase) && !key.Contains("KEYBOARD", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("CREDENTIAL", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("PRIVATE", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("CERTIFICATE", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("BEARER", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("APIKEY", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("CLIENT_SECRET", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("SIGNING_KEY", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("DB_NAME", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("DB_USER", StringComparison.OrdinalIgnoreCase);
    }
}
