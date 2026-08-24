using System.IO;
using System.Text;
using System.Windows;

namespace PrivacyAudit.Core;

/// <summary>
/// Centralized diagnostic and crash logger.
/// Writes unhandled and handled critical errors to a persistent local log file
/// in the user's application data folder without transmitting any data over network.
/// </summary>
public static class CrashLogger
{
    static readonly object SyncLock = new();
    static string? _lastDialogFingerprint;
    static DateTime _lastDialogAtUtc;

    public static string LogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotBadPrivacyDetectorAgent");

    public static string CrashLogPath =>
        Path.Combine(LogDirectory, "crash.log");

    public static void LogException(Exception ex, string context = "General")
    {
        try
        {
            lock (SyncLock)
            {
                if (!Directory.Exists(LogDirectory))
                {
                    Directory.CreateDirectory(LogDirectory);
                }

                var sb = new StringBuilder();
                sb.AppendLine("================================================================================");
                sb.AppendLine($"[CRASH / ERROR] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} (UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff})");
                sb.AppendLine($"Context: {context}");
                sb.AppendLine($"Exception Type: {ex.GetType().FullName}");
                sb.AppendLine($"Message: {ex.Message}");
                sb.AppendLine("Stack Trace:");
                sb.AppendLine(ex.StackTrace ?? "No stack trace available");

                var inner = ex.InnerException;
                int depth = 1;
                while (inner is not null && depth <= 5)
                {
                    sb.AppendLine($"--- Inner Exception #{depth} ({inner.GetType().FullName}): {inner.Message} ---");
                    sb.AppendLine(inner.StackTrace ?? "");
                    inner = inner.InnerException;
                    depth++;
                }

                sb.AppendLine("================================================================================");
                sb.AppendLine();

                File.AppendAllText(CrashLogPath, sb.ToString(), Encoding.UTF8);
                StorageLimits.TrimTextLog(CrashLogPath, StorageLimits.MaxDiagnosticLogBytes);
            }
        }
        catch
        {
            // Do not throw from the crash logger
        }
    }

    public static void ShowErrorDialog(Exception ex, string context = "Application", Window? owner = null)
    {
        LogException(ex, context);

        var fingerprint = $"{context}|{ex.GetType().FullName}|{ex.Message}|{ex.InnerException?.Message}";
        lock (SyncLock)
        {
            if (string.Equals(_lastDialogFingerprint, fingerprint, StringComparison.Ordinal) &&
                DateTime.UtcNow - _lastDialogAtUtc < TimeSpan.FromSeconds(10))
                return;
            _lastDialogFingerprint = fingerprint;
            _lastDialogAtUtc = DateTime.UtcNow;
        }

        var isRu = LocalizationService.IsRussian();
        var title = isRu ? "NotBad Privacy Detector Agent — Ошибка" : "NotBad Privacy Detector Agent — Error";
        var message = isRu
            ? $"Произошла ошибка при выполнении операции ({context}):\n\n{ex.Message}\n\nПодробности записаны в журнал сбоев:\n{CrashLogPath}"
            : $"An error occurred during operation ({context}):\n\n{ex.Message}\n\nDetails have been logged to:\n{CrashLogPath}";

        try
        {
            if (owner is not null && owner.IsVisible)
            {
                System.Windows.MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch
        {
            // Fallback if MessageBox fails
        }
    }
}
