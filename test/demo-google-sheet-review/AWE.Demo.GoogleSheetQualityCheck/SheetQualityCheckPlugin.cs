using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using AWE.Sdk.v2;
using AWE.Sdk.v2.Attributes;

namespace AWE.Demo.GoogleSheetReview;

public class SheetQualityCheckInput
{
    [Required]
    [UiField(Label = "Rows JSON", Widget = "textarea")]
    public string RowsJson { get; set; } = "[]";

    [UiField(Label = "Required columns CSV")]
    public string RequiredColumnsCsv { get; set; } = "applicantName,major,gpa,englishScore,experienceMonths,documentText";
}

public class SheetQualityCheckOutput
{
    public bool IsValid { get; set; }
    public int CheckedRows { get; set; }
    public int IssueCount { get; set; }
    public int BlankApplicantCount { get; set; }
    public int BlankDocumentCount { get; set; }
    public int InvalidGpaCount { get; set; }
    public int InvalidEnglishScoreCount { get; set; }
    public string MissingColumnsCsv { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

public class SheetQualityCheckPlugin : WorkflowPluginBase<SheetQualityCheckInput, SheetQualityCheckOutput>
{
    public override string Name => "AWE.Demo.GoogleSheetReview.SheetQualityCheckPlugin";
    public override string DisplayName => "Demo - Sheet Quality Check";
    public override string Description => "Checks required columns and common data quality issues in sheet rows.";
    public override string Category => "Google Sheet Review";
    public override string Icon => "lucide-filter";

    protected override Task<SheetQualityCheckOutput> ExecuteLogicAsync(SheetQualityCheckInput input, CancellationToken ct)
    {
        var rows = ReadRows(input.RowsJson);
        var availableColumns = rows.Count == 0
            ? Array.Empty<string>()
            : rows[0].Keys.ToArray();

        var requiredColumns = input.RequiredColumnsCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        var missingColumns = requiredColumns
            .Where(required => !availableColumns.Any(column => column.Equals(required, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var blankApplicantCount = 0;
        var blankDocumentCount = 0;
        var invalidGpaCount = 0;
        var invalidEnglishScoreCount = 0;

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(Get(row, "applicantName", "name", "fullName")))
            {
                blankApplicantCount++;
            }

            if (string.IsNullOrWhiteSpace(Get(row, "documentText", "document", "essay", "profile", "notes")))
            {
                blankDocumentCount++;
            }

            if (!TryParseDouble(Get(row, "gpa"), out _))
            {
                invalidGpaCount++;
            }

            if (!TryParseDouble(Get(row, "englishScore", "toeic", "ielts"), out _))
            {
                invalidEnglishScoreCount++;
            }
        }

        var issueCount = missingColumns.Length
            + blankApplicantCount
            + blankDocumentCount
            + invalidGpaCount
            + invalidEnglishScoreCount;

        var isValid = rows.Count > 0 && issueCount == 0;
        var summary = isValid
            ? $"Sheet quality passed for {rows.Count} rows."
            : $"Sheet quality found {issueCount} issues across {rows.Count} rows. Missing columns: {string.Join(", ", missingColumns.DefaultIfEmpty("none"))}.";

        return Task.FromResult(new SheetQualityCheckOutput
        {
            IsValid = isValid,
            CheckedRows = rows.Count,
            IssueCount = issueCount,
            BlankApplicantCount = blankApplicantCount,
            BlankDocumentCount = blankDocumentCount,
            InvalidGpaCount = invalidGpaCount,
            InvalidEnglishScoreCount = invalidEnglishScoreCount,
            MissingColumnsCsv = string.Join(",", missingColumns),
            Summary = summary
        });
    }

    private static List<Dictionary<string, string>> ReadRows(string rowsJson)
    {
        return JsonSerializer.Deserialize<List<Dictionary<string, string>>>(
            rowsJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<Dictionary<string, string>>();
    }

    private static string? Get(Dictionary<string, string> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            foreach (var pair in row)
            {
                if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }
        }

        return null;
    }

    private static bool TryParseDouble(string? value, out double result)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }
}
