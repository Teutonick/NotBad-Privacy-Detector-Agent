using PrivacyAudit.Core;

namespace PrivacyAudit.Tests;

public sealed class PiiDetectorTests
{
    [Theory]
    [InlineData("79927398713", true)] // Standard Luhn test vector
    [InlineData("49927398716", true)] // Standard Luhn test vector
    [InlineData("5555555555554444", true)] // Valid Mastercard test vector
    [InlineData("4539148803436467", true)] // Valid Visa test vector
    [InlineData("79927398710", false)] // Invalid check digit
    [InlineData("123456", false)] // Too short
    public void LuhnAlgorithm_ValidatesCreditCardNumbers(string number, bool expected)
    {
        Assert.Equal(expected, PiiDetector.IsValidLuhn(number));
    }

    [Theory]
    [InlineData("7707083893", true)] // Sberbank 10-digit INN
    [InlineData("7707083890", false)] // Invalid 10-digit INN
    [InlineData("500100732259", true)] // Valid 12-digit individual INN
    [InlineData("500100732250", false)] // Invalid 12-digit INN
    public void InnValidator_ValidatesChecksum(string inn, bool expected)
    {
        Assert.Equal(expected, PiiDetector.IsValidInn(inn));
    }

    [Theory]
    [InlineData("11223344595", true)] // Valid SNILS
    [InlineData("08765430300", true)] // Valid SNILS
    [InlineData("11223344500", false)] // Invalid control
    public void SnilsValidator_ValidatesChecksum(string snils, bool expected)
    {
        Assert.Equal(expected, PiiDetector.IsValidSnils(snils));
    }

    [Fact]
    public void PiiScan_DetectsEmailsPhonesAndFullNames()
    {
        var text = @"Контактное лицо: Иванов Иван Иванович, email: ivanov@company.ru, тел: +7 (999) 123-45-67.
Адрес доставки: г. Москва, ул. Ленина, д. 10, кв. 42.
Telegram: @ivan_tech, link: https://t.me/ivan_tech";

        var result = PiiDetector.Scan(text);

        Assert.True(result.TotalMatches >= 4);
        Assert.Contains("Email", result.Categories);
        Assert.Contains("Phone", result.Categories);
        Assert.Contains("FIO", result.Categories);
        Assert.Contains("Telegram", result.Categories);
        Assert.Contains("Address", result.Categories);
        Assert.Contains("PII_Bundle", result.Categories);
        Assert.Contains(result.Matches, x => x.Category == "Telegram" && x.Sample == "https://t.me/ivan_tech");
    }

    [Fact]
    public void PiiScan_DoesNotJoinShortNumbersAcrossLinesOrTreatTwoDigitGroupsAsCard()
    {
        var text = "8003\r\n18005\r\n18\r\n86 20 68 67 43 5 77 70 96 37 79 71 35 30 22 4";

        var result = PiiDetector.Scan(text);

        Assert.DoesNotContain(result.Matches, x => x.Category == "Phone");
        Assert.DoesNotContain(result.Matches, x => x.Category == "BankCard");
    }

    [Fact]
    public void PiiScan_DetectsConventionalGroupedPaymentCard()
    {
        var result = PiiDetector.Scan("Card: 5555 5555 5555 4444");

        Assert.Contains(result.Matches, x => x.Category == "BankCard");
    }

    [Fact]
    public void PiiScan_DoesNotTreatCodeAtMentionsAsTelegramOrEmailAsTelegram()
    {
        var result = PiiDetector.Scan("@classmethod @property @param user@example.com");

        Assert.Contains(result.Matches, x => x.Category == "Email" && x.Sample == "user@example.com");
        Assert.DoesNotContain(result.Matches, x => x.Category == "Telegram");
    }

    [Fact]
    public void PiiScan_DoesNotTreatStandaloneDatesAsBirthDates()
    {
        var result = PiiDetector.Scan("Релизы: 20.04.2026, 21.01.2026. План работ до 01.02.2026.");

        Assert.DoesNotContain("BirthDate", result.Categories);
        Assert.DoesNotContain(result.Matches, x => x.Category == "BirthDate");
        Assert.Equal(0, result.TotalMatches);
    }

    [Fact]
    public void PiiResult_LegacyBirthDatesAreRemovedWhenMetadataIsRead()
    {
        const string metadata = """{"pii_scan":{"status":"completed","total_matches":2,"categories":["BirthDate","Email"],"matches":[{"category":"BirthDate","sample":"20.04.2026","confidence":0.75},{"category":"Email","sample":"user@example.com","confidence":0.95}]}}""";

        Assert.True(PiiDetectionResult.TryParse(metadata, out var result));
        Assert.Equal(1, result!.TotalMatches);
        Assert.DoesNotContain("BirthDate", result.Categories);
        Assert.DoesNotContain(result.Matches, x => x.Category == "BirthDate");
        Assert.Contains(result.Matches, x => x.Category == "Email");
    }

    [Fact]
    public void PiiScan_EmptyOrNull_ReturnsEmptyResult()
    {
        var res1 = PiiDetector.Scan("");
        var res2 = PiiDetector.Scan("   ");
        Assert.Equal(0, res1.TotalMatches);
        Assert.Equal(0, res2.TotalMatches);
    }

    [Fact]
    public void PiiScan_LargeOrComplexText_ExecutesSafely()
    {
        var complex = string.Concat(Enumerable.Repeat("abc 1234-5678-9012-3456 test@sample.com +7 999 000 00 00 ", 1000));
        var result = PiiDetector.Scan(complex);
        Assert.NotNull(result);
        Assert.True(result.TotalMatches > 0);
    }

    [Fact]
    public void CrashLogger_LogException_WritesSafelyWithoutCrashing()
    {
        var ex = new InvalidOperationException("Test crash exception for unit test", new ArgumentNullException("param"));
        CrashLogger.LogException(ex, "UnitTest");
        Assert.True(File.Exists(CrashLogger.CrashLogPath));
    }
}
