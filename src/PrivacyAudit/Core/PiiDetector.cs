using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PrivacyAudit.Core;

public sealed record PiiMatchItem(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("sample")] string Sample,
    [property: JsonPropertyName("confidence")] double Confidence
);

public sealed class PiiDetectionResult
{
    [JsonPropertyName("status")] public string Status { get; set; } = "completed";
    [JsonPropertyName("total_matches")] public int TotalMatches { get; set; }
    [JsonPropertyName("categories")] public List<string> Categories { get; set; } = [];
    [JsonPropertyName("matches")] public List<PiiMatchItem> Matches { get; set; } = [];
    [JsonPropertyName("scanned_at_utc")] public DateTime ScannedAtUtc { get; set; } = DateTime.UtcNow;

    public static string Serialize(PiiDetectionResult result) => JsonSerializer.Serialize(result);

    public static bool TryParse(string? json, out PiiDetectionResult? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("pii_scan", out var piiProp)) return false;
            result = JsonSerializer.Deserialize<PiiDetectionResult>(piiProp.GetRawText());
            if (result is not null)
            {
                result.Matches.RemoveAll(x => x.Category.Equals("BirthDate", StringComparison.OrdinalIgnoreCase));
                result.Categories.RemoveAll(x => x.Equals("BirthDate", StringComparison.OrdinalIgnoreCase));
                result.TotalMatches = result.Matches.Count;
            }
            return result is not null;
        }
        catch
        {
            return false;
        }
    }

    public static string InjectIntoMetadata(string currentJson, PiiDetectionResult piiResult)
    {
        try
        {
            var dict = string.IsNullOrWhiteSpace(currentJson)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(currentJson) ?? new();
            dict["pii_scan"] = piiResult;
            return JsonSerializer.Serialize(dict);
        }
        catch
        {
            return JsonSerializer.Serialize(new { pii_scan = piiResult });
        }
    }
}

public static class PiiDetector
{
    static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);

    static readonly Regex EmailRegex = new(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled, RegexTimeout);
    // Phone separators intentionally exclude CR/LF. A column of short numbers must not be
    // concatenated into a phone number by the regex engine.
    static readonly Regex PhoneRegex = new(@"(?<!\d)(?:(?:\+7|8)[ \t\-]?(?:\(\d{3}\)|\d{3})[ \t\-]?\d{3}[ \t\-]?\d{2}[ \t\-]?\d{2}|\+[1-9]\d{0,2}[ \t\-]?\(?\d{2,4}\)?[ \t\-]?\d{3,4}[ \t\-]?\d{2,4})(?!\d)", RegexOptions.Compiled, RegexTimeout);
    // Only contiguous card numbers or conventional four-digit groups are candidates.
    // This rejects arbitrary data such as "86 20 68 67 ..." before Luhn is evaluated.
    static readonly Regex CardContiguousRegex = new(@"(?<!\d)\d{13,19}(?!\d)", RegexOptions.Compiled, RegexTimeout);
    static readonly Regex CardGroupedRegex = new(@"(?<!\d)\d{4}(?:[ -]\d{4}){3}(?![ -]?\d)", RegexOptions.Compiled, RegexTimeout);
    static readonly Regex DigitsOnlyRegex = new(@"\D", RegexOptions.Compiled, RegexTimeout);
    static readonly Regex InnCandidateRegex = new(@"\b\d{10}\b|\b\d{12}\b", RegexOptions.Compiled, RegexTimeout);
    static readonly Regex SnilsCandidateRegex = new(@"\b\d{3}[-\s]?\d{3}[-\s]?\d{3}[-\s]?\d{2}\b", RegexOptions.Compiled, RegexTimeout);
    static readonly Regex PassportCandidateRegex = new(@"\b\d{2}\s?\d{2}\s?\d{6}\b", RegexOptions.Compiled, RegexTimeout);
    static readonly Regex IpRegex = new(@"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b", RegexOptions.Compiled, RegexTimeout);
    static readonly Regex MacRegex = new(@"\b(?:[0-9A-Fa-f]{2}[:-]){5}(?:[0-9A-Fa-f]{2})\b", RegexOptions.Compiled, RegexTimeout);
    // A naked @name is ambiguous in source code and prose. Only an explicit Telegram URL
    // is treated as a Telegram identifier; email detection remains independent.
    static readonly Regex TelegramRegex = new(@"https?:\/\/(?:www\.)?t\.me\/[a-zA-Z0-9_]{5,32}(?![a-zA-Z0-9_])", RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexTimeout);
    static readonly Regex FioRegex = new(@"\b[А-ЯЁ][а-яё]+(?:-[А-ЯЁ][а-яё]+)?\s+[А-ЯЁ][а-яё]+\s+[А-ЯЁ][а-яё]+(?:ович|евич|ич|овна|евна|ична|инична)\b", RegexOptions.Compiled, RegexTimeout);
    static readonly Regex AddressRegex = new(@"(?i)\b(?:г\.|город|обл\.|область|ул\.|улица|пр-т|проспект|пер\.|переулок|д\.|дом|кв\.|квартира)\s+[А-ЯЁа-яё0-9\s.,\-/]{4,40}\b", RegexOptions.Compiled, RegexTimeout);

    public static PiiDetectionResult Scan(string text)
    {
        var result = new PiiDetectionResult();
        if (string.IsNullOrWhiteSpace(text)) return result;

        try
        {
            var matches = new List<PiiMatchItem>();
            var foundCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Bank Cards (with Luhn validation)
            try
            {
                foreach (Match m in CardContiguousRegex.Matches(text).Cast<Match>().Concat(CardGroupedRegex.Matches(text)))
                {
                    var raw = DigitsOnlyRegex.Replace(m.Value, "");
                    if (raw.Length is >= 13 and <= 19 && IsPlausiblePaymentCard(raw) && IsValidLuhn(raw))
                    {
                        var masked = MaskCard(raw);
                        matches.Add(new PiiMatchItem("BankCard", masked, 0.99));
                        foundCategories.Add("BankCard");
                        if (matches.Count >= 50) break;
                    }
                }
            }
            catch { }

            // 2. INN (10 or 12 digits with checksums)
            try
            {
                foreach (Match m in InnCandidateRegex.Matches(text))
                {
                    var raw = m.Value;
                    if (IsValidInn(raw))
                    {
                        matches.Add(new PiiMatchItem("INN", raw, 0.95));
                        foundCategories.Add("INN");
                        if (matches.Count >= 50) break;
                    }
                }
            }
            catch { }

            // 3. SNILS (with checksum)
            try
            {
                foreach (Match m in SnilsCandidateRegex.Matches(text))
                {
                    var raw = DigitsOnlyRegex.Replace(m.Value, "");
                    if (raw.Length == 11 && IsValidSnils(raw))
                    {
                        var formatted = $"{raw[..3]}-{raw[3..6]}-{raw[6..9]} {raw[9..]}";
                        matches.Add(new PiiMatchItem("SNILS", formatted, 0.98));
                        foundCategories.Add("SNILS");
                        if (matches.Count >= 50) break;
                    }
                }
            }
            catch { }

            // 4. Passports (RF Series & Number)
            try
            {
                foreach (Match m in PassportCandidateRegex.Matches(text))
                {
                    var raw = DigitsOnlyRegex.Replace(m.Value, "");
                    if (raw.Length == 10)
                    {
                        if (int.TryParse(raw[..2], out var seriesRegion) && seriesRegion is >= 1 and <= 99)
                        {
                            var masked = $"{raw[..2]} {raw[2..4]} ******";
                            matches.Add(new PiiMatchItem("Passport", masked, 0.88));
                            foundCategories.Add("Passport");
                            if (matches.Count >= 50) break;
                        }
                    }
                }
            }
            catch { }

            // 5. Emails
            try
            {
                foreach (Match m in EmailRegex.Matches(text))
                {
                    matches.Add(new PiiMatchItem("Email", m.Value, 0.95));
                    foundCategories.Add("Email");
                    if (matches.Count >= 50) break;
                }
            }
            catch { }

            // 6. Phone Numbers
            try
            {
                foreach (Match m in PhoneRegex.Matches(text))
                {
                    var digits = DigitsOnlyRegex.Replace(m.Value, "");
                    if (digits.Length is >= 10 and <= 15)
                    {
                        matches.Add(new PiiMatchItem("Phone", m.Value.Trim(), 0.90));
                        foundCategories.Add("Phone");
                        if (matches.Count >= 50) break;
                    }
                }
            }
            catch { }

            // 7. Full Names (ФИО)
            try
            {
                foreach (Match m in FioRegex.Matches(text))
                {
                    matches.Add(new PiiMatchItem("FIO", m.Value.Trim(), 0.92));
                    foundCategories.Add("FIO");
                    if (matches.Count >= 50) break;
                }
            }
            catch { }

            // 8. Telegram Usernames
            try
            {
                foreach (Match m in TelegramRegex.Matches(text))
                {
                    if (!m.Value.Contains("example", StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(new PiiMatchItem("Telegram", m.Value.Trim(), 0.85));
                        foundCategories.Add("Telegram");
                        if (matches.Count >= 50) break;
                    }
                }
            }
            catch { }

            // 9. IP / MAC
            try
            {
                foreach (Match m in IpRegex.Matches(text))
                {
                    if (!m.Value.StartsWith("127.", StringComparison.Ordinal) && m.Value != "0.0.0.0" && m.Value != "255.255.255.255")
                    {
                        matches.Add(new PiiMatchItem("IP", m.Value, 0.80));
                        foundCategories.Add("IP");
                        if (matches.Count >= 50) break;
                    }
                }
            }
            catch { }

            try
            {
                foreach (Match m in MacRegex.Matches(text))
                {
                    matches.Add(new PiiMatchItem("MAC", m.Value, 0.90));
                    foundCategories.Add("MAC");
                    if (matches.Count >= 50) break;
                }
            }
            catch { }

            // 10. Addresses. Standalone dates are intentionally ignored because a date
            // without identity context is usually document metadata, a deadline, or a release date.
            try
            {
                foreach (Match m in AddressRegex.Matches(text))
                {
                    matches.Add(new PiiMatchItem("Address", m.Value.Trim(), 0.75));
                    foundCategories.Add("Address");
                    if (matches.Count >= 50) break;
                }
            }
            catch { }

            // 11. Bundle check (FIO + Contact info)
            if (foundCategories.Contains("FIO") && (foundCategories.Contains("Phone") || foundCategories.Contains("Email") || foundCategories.Contains("Passport")))
            {
                foundCategories.Add("PII_Bundle");
            }

            result.Matches = matches;
            result.TotalMatches = matches.Count;
            result.Categories = foundCategories.ToList();
        }
        catch
        {
            // Safeguard against any unexpected pattern exceptions
        }

        return result;
    }

    public static bool IsValidLuhn(string number)
    {
        if (string.IsNullOrWhiteSpace(number) || number.Length < 8 || number.Length > 28) return false;
        int sum = 0;
        bool alternate = false;
        for (int i = number.Length - 1; i >= 0; i--)
        {
            if (!char.IsDigit(number[i])) return false;
            int d = number[i] - '0';
            if (alternate)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
            alternate = !alternate;
        }
        return sum % 10 == 0;
    }

    public static bool IsValidInn(string inn)
    {
        if (string.IsNullOrWhiteSpace(inn) || !inn.All(char.IsDigit)) return false;
        if (inn.Length == 10)
        {
            int[] weights = [2, 4, 10, 3, 5, 9, 4, 6, 8, 0];
            int sum = 0;
            for (int i = 0; i < 10; i++) sum += (inn[i] - '0') * weights[i];
            int check = sum % 11 % 10;
            return check == (inn[9] - '0');
        }
        if (inn.Length == 12)
        {
            int[] weights11 = [7, 2, 4, 10, 3, 5, 9, 4, 6, 8, 0];
            int[] weights12 = [3, 7, 2, 4, 10, 3, 5, 9, 4, 6, 8, 0];

            int sum11 = 0;
            for (int i = 0; i < 11; i++) sum11 += (inn[i] - '0') * weights11[i];
            int check11 = sum11 % 11 % 10;
            if (check11 != (inn[10] - '0')) return false;

            int sum12 = 0;
            for (int i = 0; i < 12; i++) sum12 += (inn[i] - '0') * weights12[i];
            int check12 = sum12 % 11 % 10;
            return check12 == (inn[11] - '0');
        }
        return false;
    }

    public static bool IsValidSnils(string snils)
    {
        if (string.IsNullOrWhiteSpace(snils) || snils.Length != 11 || !snils.All(char.IsDigit)) return false;
        long mainPart = long.Parse(snils[..9]);
        if (mainPart <= 1001998) return true; // Historical SNILS before algorithm

        int sum = 0;
        for (int i = 0; i < 9; i++)
        {
            sum += (snils[i] - '0') * (9 - i);
        }

        int check;
        if (sum < 100) check = sum;
        else if (sum is 100 or 101) check = 0;
        else
        {
            check = sum % 101;
            if (check == 100) check = 0;
        }

        int controlDigits = int.Parse(snils[9..]);
        return check == controlDigits;
    }

    static string MaskCard(string card)
    {
        if (card.Length < 10) return "****";
        return $"{card[..4]} **** **** {card[^4..]}";
    }

    static bool IsPlausiblePaymentCard(string number)
    {
        if (string.IsNullOrWhiteSpace(number) || !number.All(char.IsDigit)) return false;

        // Keep the detector focused on common payment-network ranges. Luhn alone is
        // insufficient for random numeric data because roughly one in ten candidates pass it.
        if (number.Length == 15) return number.StartsWith("34", StringComparison.Ordinal) || number.StartsWith("37", StringComparison.Ordinal);
        if (number[0] == '4') return number.Length is >= 13 and <= 19; // Visa
        if (number.Length == 16 && int.TryParse(number[..2], out var twoDigitPrefix) && twoDigitPrefix is >= 51 and <= 55) return true; // Mastercard
        if (number.Length == 16 && int.TryParse(number[..4], out var fourDigitPrefix) && fourDigitPrefix is >= 2221 and <= 2720) return true; // Mastercard 2-series
        if (number.Length == 16 && (number.StartsWith("6011", StringComparison.Ordinal) || number.StartsWith("65", StringComparison.Ordinal) || number.StartsWith("35", StringComparison.Ordinal))) return true; // Discover/JCB
        if (number.Length is >= 16 and <= 19 && number.StartsWith("62", StringComparison.Ordinal)) return true; // UnionPay
        if (number.Length == 16 && number.StartsWith("220", StringComparison.Ordinal)) return true; // MIR
        return false;
    }
}
