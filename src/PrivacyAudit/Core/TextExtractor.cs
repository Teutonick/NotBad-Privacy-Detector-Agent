using System.IO.Compression;
using System.Text;
using System.Xml;

namespace PrivacyAudit.Core;

public static class TextExtractor
{
    const int MaxReadBytes = 5 * 1024 * 1024; // 5 MB safe read limit per file

    static readonly HashSet<string> PlainTextExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".csv", ".tsv", ".json", ".xml", ".yaml", ".yml", ".log", ".md",
        ".ini", ".cfg", ".conf", ".env", ".sql", ".sh", ".bat", ".cmd", ".ps1",
        ".py", ".cs", ".js", ".ts", ".jsx", ".tsx", ".java", ".cpp", ".c", ".h",
        ".hpp", ".go", ".rs", ".php", ".rb", ".html", ".htm", ".css", ".scss",
        ".properties", ".toml", ".config", ".rc", ".reg"
    };

    public static bool IsSupported(string path)
    {
        var ext = Path.GetExtension(path);
        return PlainTextExt.Contains(ext) ||
               ext.Equals(".docx", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".pptx", StringComparison.OrdinalIgnoreCase);
    }

    public static string ExtractText(string path)
    {
        if (!File.Exists(path)) return "";
        try
        {
            var ext = Path.GetExtension(path);
            if (ext.Equals(".docx", StringComparison.OrdinalIgnoreCase))
                return ExtractFromDocx(path);
            if (ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                return ExtractFromXlsx(path);
            if (ext.Equals(".pptx", StringComparison.OrdinalIgnoreCase))
                return ExtractFromPptx(path);

            return ExtractFromPlainText(path);
        }
        catch
        {
            return "";
        }
    }

    public static string ExtractFromPlainText(string path)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists || fi.Length == 0) return "";

        var bytesToRead = (int)Math.Min(fi.Length, MaxReadBytes);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[bytesToRead];
        var read = stream.Read(buffer, 0, bytesToRead);
        if (read == 0) return "";

        // Detect encoding or default to UTF-8
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    public static string ExtractFromDocx(string path)
    {
        var sb = new StringBuilder();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        foreach (var entryName in new[] { "word/document.xml", "word/footnotes.xml", "word/endnotes.xml" })
        {
            var entry = zip.GetEntry(entryName);
            if (entry is null) continue;

            using var entryStream = entry.Open();
            using var reader = XmlReader.Create(entryStream, new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Ignore });
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
                {
                    var text = reader.ReadElementContentAsString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        sb.Append(text).Append(' ');
                    }
                }
            }
        }
        return sb.ToString();
    }

    public static string ExtractFromXlsx(string path)
    {
        var sb = new StringBuilder();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        // Read shared strings table
        var sharedStrings = zip.GetEntry("xl/sharedStrings.xml");
        if (sharedStrings is not null)
        {
            using var entryStream = sharedStrings.Open();
            using var reader = XmlReader.Create(entryStream, new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Ignore });
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
                {
                    var text = reader.ReadElementContentAsString();
                    if (!string.IsNullOrWhiteSpace(text))
                        sb.Append(text).Append(' ');
                }
            }
        }

        // Read sheets text / values
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            using var entryStream = entry.Open();
            using var reader = XmlReader.Create(entryStream, new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Ignore });
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && (reader.LocalName == "v" || reader.LocalName == "t"))
                {
                    var text = reader.ReadElementContentAsString();
                    if (!string.IsNullOrWhiteSpace(text))
                        sb.Append(text).Append(' ');
                }
            }
        }
        return sb.ToString();
    }

    public static string ExtractFromPptx(string path)
    {
        var sb = new StringBuilder();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            using var entryStream = entry.Open();
            using var reader = XmlReader.Create(entryStream, new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Ignore });
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
                {
                    var text = reader.ReadElementContentAsString();
                    if (!string.IsNullOrWhiteSpace(text))
                        sb.Append(text).Append(' ');
                }
            }
        }
        return sb.ToString();
    }
}
