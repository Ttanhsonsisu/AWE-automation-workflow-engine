using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using AWE.Sdk.v2;
using AWE.Sdk.v2.Attributes;

namespace AWE.Demo.GoogleSheetReview;

public class ReadGoogleSheetInput
{
    [Required]
    [UiField(Label = "Google Sheets link")]
    public string SheetUrl { get; set; } = string.Empty;

    [UiField(Label = "Sheet GID")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Gid { get; set; }

    [Range(1, 1000)]
    [UiField(Label = "Max data rows")]
    public int MaxRows { get; set; } = 200;

    [UiField(Label = "First row is header")]
    public bool HasHeader { get; set; } = true;
}

public class ReadGoogleSheetOutput
{
    public string SourceKind { get; set; } = string.Empty;
    public string SheetId { get; set; } = string.Empty;
    public string Gid { get; set; } = "0";
    public int RowCount { get; set; }
    public string ColumnsCsv { get; set; } = string.Empty;
    public string RowsJson { get; set; } = "[]";
    public string Summary { get; set; } = string.Empty;
}

public class ReadGoogleSheetPlugin : WorkflowPluginBase<ReadGoogleSheetInput, ReadGoogleSheetOutput>
{
    private static readonly Regex GoogleSheetIdRegex = new(
        @"docs\.google\.com/spreadsheets/d/([^/?#]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex GidRegex = new(
        @"(?:[?#&]|#)gid=([0-9]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public override string Name => "AWE.Demo.GoogleSheetReview.ReadGoogleSheetPlugin";
    public override string DisplayName => "Demo - Read Google Sheet";
    public override string Description => "Reads a public Google Sheets document through its CSV export endpoint.";
    public override string Category => "Google Sheet Review";
    public override string Icon => "lucide-database";

    protected override async Task<ReadGoogleSheetOutput> ExecuteLogicAsync(ReadGoogleSheetInput input, CancellationToken ct)
    {
        var source = ResolveCsvSource(input.SheetUrl, input.Gid.ToString());
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AWE-Demo-GoogleSheetReview/1.0");

        var csv = await DownloadCsvWithFallbackAsync(client, source, ct);

        var rows = ParseCsv(csv);
        if (rows.Count == 0)
        {
            throw new InvalidOperationException("The sheet did not contain any rows.");
        }

        var columns = input.HasHeader
            ? EnsureUniqueColumns(rows[0].Select(NormalizeColumnName).ToList())
            : Enumerable.Range(1, rows.Max(r => r.Count)).Select(i => $"Column{i}").ToList();

        var startIndex = input.HasHeader ? 1 : 0;
        var records = new List<Dictionary<string, string>>();

        foreach (var row in rows.Skip(startIndex).Take(input.MaxRows))
        {
            if (row.Count == 1 && string.IsNullOrWhiteSpace(row[0]))
            {
                continue;
            }

            var record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < columns.Count; i++)
            {
                record[columns[i]] = i < row.Count ? row[i].Trim() : string.Empty;
            }

            records.Add(record);
        }

        return new ReadGoogleSheetOutput
        {
            SourceKind = source.SourceKind,
            SheetId = source.SheetId,
            Gid = source.Gid,
            RowCount = records.Count,
            ColumnsCsv = string.Join(",", columns),
            RowsJson = JsonSerializer.Serialize(records),
            Summary = $"Loaded {records.Count} data rows from Google Sheet gid {source.Gid}."
        };
    }

    private static async Task<string> DownloadCsvWithFallbackAsync(HttpClient client, CsvSource source, CancellationToken ct)
    {
        var errors = new List<string>();

        foreach (var url in source.CsvUrls)
        {
            try
            {
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                var csv = await response.Content.ReadAsStringAsync(ct);

                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw new InvalidOperationException("Google Sheet is not public. Share it as 'Anyone with the link can view' or publish it to the web.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"Status {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                if (LooksLikeHtml(csv))
                {
                    throw new InvalidOperationException("Google returned HTML instead of CSV.");
                }

                return csv;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is InvalidOperationException)
            {
                errors.Add($"{ShortenUrl(url)} -> {ex.Message}");
            }
        }

        throw new InvalidOperationException(
            "Could not read Google Sheet as CSV. Tried gviz/export/published CSV endpoints. " +
            "Make sure the sheet is shared as 'Anyone with the link can view'. Details: " +
            string.Join(" | ", errors));
    }

    private static CsvSource ResolveCsvSource(string sheetUrl, string fallbackGid)
    {
        var trimmed = sheetUrl.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException("SheetUrl is required.");
        }

        if (trimmed.Contains("output=csv", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("format=csv", StringComparison.OrdinalIgnoreCase)
            || trimmed.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return new CsvSource([trimmed], "PublishedCsv", string.Empty, ExtractGid(trimmed, fallbackGid));
        }

        var idMatch = GoogleSheetIdRegex.Match(trimmed);
        if (!idMatch.Success)
        {
            throw new InvalidOperationException("SheetUrl must be a Google Sheets link or a direct CSV export URL.");
        }

        var sheetId = idMatch.Groups[1].Value;
        var gid = ExtractGid(trimmed, fallbackGid);

        // Prefer the Visualization API because it normally serves CSV from docs.google.com
        // directly. The export endpoint often redirects to doc-*-sheets.googleusercontent.com,
        // which is blocked or times out in some local Docker/self-host networks.
        var gvizUrl = $"https://docs.google.com/spreadsheets/d/{sheetId}/gviz/tq?tqx=out:csv&gid={gid}";
        var exportUrl = $"https://docs.google.com/spreadsheets/d/{sheetId}/export?format=csv&gid={gid}";
        var publishedUrl = $"https://docs.google.com/spreadsheets/d/{sheetId}/pub?gid={gid}&single=true&output=csv";

        return new CsvSource([gvizUrl, exportUrl, publishedUrl], "GoogleSheetsExport", sheetId, gid);
    }

    private static string ExtractGid(string url, string fallback)
    {
        var match = GidRegex.Match(url);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        return string.IsNullOrWhiteSpace(fallback) ? "0" : fallback.Trim();
    }

    private static bool LooksLikeHtml(string content)
    {
        var trimmed = content.TrimStart();
        return trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("ServiceLogin", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeColumnName(string name)
    {
        var normalized = name.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "Column";
        }

        var builder = new StringBuilder();
        var capitalizeNext = false;
        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(capitalizeNext ? char.ToUpperInvariant(ch) : ch);
                capitalizeNext = false;
            }
            else
            {
                capitalizeNext = builder.Length > 0;
            }
        }

        return builder.Length == 0 ? "Column" : builder.ToString();
    }

    private static List<string> EnsureUniqueColumns(List<string> columns)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var column in columns)
        {
            var name = string.IsNullOrWhiteSpace(column) ? "Column" : column;
            seen.TryGetValue(name, out var count);
            seen[name] = count + 1;
            result.Add(count == 0 ? name : $"{name}{count + 1}");
        }

        return result;
    }

    private static List<List<string>> ParseCsv(string csv)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var ch = csv[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }

                continue;
            }

            if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (ch == '\r' || ch == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = new List<string>();

                if (ch == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                {
                    i++;
                }
            }
            else
            {
                field.Append(ch);
            }
        }

        row.Add(field.ToString());
        if (row.Count > 1 || !string.IsNullOrWhiteSpace(row[0]))
        {
            rows.Add(row);
        }

        return rows;
    }

    private static string ShortenUrl(string url)
    {
        const int max = 96;
        return url.Length <= max ? url : $"{url[..max]}...";
    }

    private sealed record CsvSource(IReadOnlyList<string> CsvUrls, string SourceKind, string SheetId, string Gid);
}
