using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using AWE.Sdk.v2;
using AWE.Sdk.v2.Attributes;

namespace AWE.Demo.GoogleSheetReview;

public class AnalyzeSheetApplicationsInput
{
    [Required]
    [UiField(Label = "Rows JSON", Widget = "textarea")]
    public string RowsJson { get; set; } = "[]";

    [Range(0, 4)]
    [UiField(Label = "Minimum GPA")]
    public double MinimumGpa { get; set; } = 2.8;

    [Range(0, 990)]
    [UiField(Label = "Minimum English score")]
    public int MinimumEnglishScore { get; set; } = 550;

    [Range(0, 120)]
    [UiField(Label = "Target experience months")]
    public int TargetExperienceMonths { get; set; } = 6;
}

public class AnalyzeSheetApplicationsOutput
{
    public int StrongCount { get; set; }
    public int ReviewCount { get; set; }
    public int RejectCount { get; set; }
    public bool RequiresApproval { get; set; }
    public string DecisionMode { get; set; } = "AUTO_COMPLETE";
    public string ResultsJson { get; set; } = "[]";
    public string Summary { get; set; } = string.Empty;
}

public class ApplicationAnalysisRow
{
    public int RowNumber { get; set; }
    public string ApplicantName { get; set; } = string.Empty;
    public string Major { get; set; } = string.Empty;
    public double Gpa { get; set; }
    public int EnglishScore { get; set; }
    public int ExperienceMonths { get; set; }
    public int CompositeScore { get; set; }
    public string Decision { get; set; } = "REVIEW";
    public string Reason { get; set; } = string.Empty;
}

public class AnalyzeSheetApplicationsPlugin : WorkflowPluginBase<AnalyzeSheetApplicationsInput, AnalyzeSheetApplicationsOutput>
{
    public override string Name => "AWE.Demo.GoogleSheetReview.AnalyzeSheetApplicationsPlugin";
    public override string DisplayName => "Demo - Analyze Sheet Applications";
    public override string Description => "Scores every application row and classifies it as STRONG, REVIEW, or REJECT.";
    public override string Category => "Google Sheet Review";
    public override string Icon => "lucide-clipboard-check";

    protected override Task<AnalyzeSheetApplicationsOutput> ExecuteLogicAsync(AnalyzeSheetApplicationsInput input, CancellationToken ct)
    {
        var rows = ReadRows(input.RowsJson);
        var results = new List<ApplicationAnalysisRow>();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var applicantName = Get(row, "applicantName", "name", "fullName") ?? $"Row {i + 1}";
            var major = Get(row, "major", "program") ?? string.Empty;
            var documentText = Get(row, "documentText", "document", "essay", "profile", "notes") ?? string.Empty;

            var gpa = ReadDouble(row, "gpa");
            var englishScore = (int)Math.Round(ReadDouble(row, "englishScore", "toeic", "ielts"));
            var experienceMonths = (int)Math.Round(ReadDouble(row, "experienceMonths", "experience", "monthsExperience"));
            var compositeScore = ScoreApplication(gpa, englishScore, experienceMonths, documentText, input);
            var hasCriticalIssue = ContainsAny(documentText, "fake", "forged", "tampered", "invalid", "placeholder", "todo");

            var decision = "REVIEW";
            var reason = "Needs human review because at least one signal is near the threshold.";

            if (hasCriticalIssue || gpa < input.MinimumGpa - 0.4 || englishScore < input.MinimumEnglishScore - 100)
            {
                decision = "REJECT";
                reason = hasCriticalIssue
                    ? "Document contains a critical risk keyword."
                    : "Academic or English score is far below the minimum threshold.";
            }
            else if (gpa >= input.MinimumGpa
                     && englishScore >= input.MinimumEnglishScore
                     && compositeScore >= 75)
            {
                decision = "STRONG";
                reason = "Core scores meet the configured thresholds.";
            }

            results.Add(new ApplicationAnalysisRow
            {
                RowNumber = i + 2,
                ApplicantName = applicantName,
                Major = major,
                Gpa = Math.Round(gpa, 2),
                EnglishScore = englishScore,
                ExperienceMonths = experienceMonths,
                CompositeScore = compositeScore,
                Decision = decision,
                Reason = reason
            });
        }

        var strongCount = results.Count(x => x.Decision == "STRONG");
        var reviewCount = results.Count(x => x.Decision == "REVIEW");
        var rejectCount = results.Count(x => x.Decision == "REJECT");
        var requiresApproval = reviewCount > 0 || rejectCount > 0;

        return Task.FromResult(new AnalyzeSheetApplicationsOutput
        {
            StrongCount = strongCount,
            ReviewCount = reviewCount,
            RejectCount = rejectCount,
            RequiresApproval = requiresApproval,
            DecisionMode = requiresApproval ? "APPROVAL_REQUIRED" : "AUTO_COMPLETE",
            ResultsJson = JsonSerializer.Serialize(results),
            Summary = $"Analyzed {results.Count} rows: {strongCount} strong, {reviewCount} review, {rejectCount} reject."
        });
    }

    private static int ScoreApplication(
        double gpa,
        int englishScore,
        int experienceMonths,
        string documentText,
        AnalyzeSheetApplicationsInput input)
    {
        var gpaScore = Math.Clamp(gpa / Math.Max(0.01, input.MinimumGpa) * 35, 0, 40);
        var englishScorePart = Math.Clamp(englishScore / (double)Math.Max(1, input.MinimumEnglishScore) * 25, 0, 30);
        var experienceScore = Math.Clamp(experienceMonths / (double)Math.Max(1, input.TargetExperienceMonths) * 15, 0, 15);
        var documentScore = Math.Clamp(documentText.Length / 300.0 * 15, 0, 15);
        return (int)Math.Round(gpaScore + englishScorePart + experienceScore + documentScore);
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

    private static double ReadDouble(Dictionary<string, string> row, params string[] keys)
    {
        var value = Get(row, keys);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;
    }

    private static bool ContainsAny(string text, params string[] terms)
    {
        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
