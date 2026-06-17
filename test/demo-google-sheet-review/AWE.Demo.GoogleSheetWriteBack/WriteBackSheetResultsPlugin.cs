using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using System.Text.Json;
using AWE.Sdk.v2;
using AWE.Sdk.v2.Attributes;

namespace AWE.Demo.GoogleSheetReview;

public class WriteBackSheetResultsInput
{
    [Required]
    [UiField(Label = "Google Sheets link")]
    public string SheetUrl { get; set; } = string.Empty;

    [Required]
    [UiField(Label = "Results JSON", Widget = "textarea")]
    public string ResultsJson { get; set; } = "[]";

    [UiField(Label = "Quality is valid")]
    public bool QualityIsValid { get; set; } = true;

    [UiField(Label = "Quality summary", Widget = "textarea")]
    public string QualitySummary { get; set; } = string.Empty;

    [UiField(Label = "Apps Script webhook URL")]
    public string AppsScriptWebhookUrl { get; set; } = string.Empty;

    [UiField(Label = "Dry run only")]
    public bool DryRun { get; set; } = true;

    [UiField(Label = "Wait for webhook response")]
    public bool WaitForWebhookResponse { get; set; } = false;

    [Range(1, 60)]
    [UiField(Label = "Webhook timeout seconds")]
    public int WebhookTimeoutSeconds { get; set; } = 8;
}

public class WriteBackSheetResultsOutput
{
    public string WriteStatus { get; set; } = "DRY_RUN";
    public int RowsPrepared { get; set; }
    public int StrongCount { get; set; }
    public int ReviewCount { get; set; }
    public int RejectCount { get; set; }
    public bool RequiresApproval { get; set; }
    public string DecisionMode { get; set; } = "AUTO_COMPLETE";
    public string Summary { get; set; } = string.Empty;
    public int WebhookStatusCode { get; set; }
    public string? WebhookMode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorDetails { get; set; }
}

public class WriteBackSheetResultsPlugin : WorkflowPluginBase<WriteBackSheetResultsInput, WriteBackSheetResultsOutput>
{
    public override string Name => "AWE.Demo.GoogleSheetReview.WriteBackSheetResultsPlugin";
    public override string DisplayName => "Demo - Write Back Sheet Results";
    public override string Description => "Prepares review results and optionally posts them to a Google Apps Script webhook.";
    public override string Category => "Google Sheet Review";
    public override string Icon => "lucide-send";

    protected override async Task<WriteBackSheetResultsOutput> ExecuteLogicAsync(WriteBackSheetResultsInput input, CancellationToken ct)
    {
        var stats = InspectResults(input.ResultsJson);
        var requiresApproval = !input.QualityIsValid || stats.ReviewCount > 0 || stats.RejectCount > 0;
        var decisionMode = requiresApproval ? "APPROVAL_REQUIRED" : "AUTO_COMPLETE";

        var output = new WriteBackSheetResultsOutput
        {
            RowsPrepared = stats.RowsPrepared,
            StrongCount = stats.StrongCount,
            ReviewCount = stats.ReviewCount,
            RejectCount = stats.RejectCount,
            RequiresApproval = requiresApproval,
            DecisionMode = decisionMode,
            Summary = BuildSummary(input, stats, "DRY_RUN", decisionMode)
        };

        if (input.DryRun || string.IsNullOrWhiteSpace(input.AppsScriptWebhookUrl))
        {
            output.WriteStatus = "DRY_RUN";
            if (input.DryRun && string.IsNullOrWhiteSpace(input.AppsScriptWebhookUrl))
            {
                output.Summary = "DRY_RUN: Skipped posting because DryRun is true AND AppsScriptWebhookUrl is empty.";
            }
            else if (input.DryRun)
            {
                output.Summary = "DRY_RUN: Skipped posting because DryRun is true.";
            }
            else
            {
                output.Summary = "DRY_RUN: Skipped posting because AppsScriptWebhookUrl is empty.";
            }
            return output;
        }

        try
        {
            // Trích xuất name và major từ dòng đầu tiên làm dự phòng cho user script
            string firstName = string.Empty;
            string firstMajor = string.Empty;
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(input.ResultsJson) ? "[]" : input.ResultsJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    var firstItem = doc.RootElement[0];
                    firstName = ReadProperty(firstItem, "ApplicantName");
                    firstMajor = ReadProperty(firstItem, "Major");
                }
            }
            catch { }

            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(Math.Clamp(input.WebhookTimeoutSeconds, 1, 60))
            };
            var payload = new
            {
                sheetUrl = input.SheetUrl,
                qualityIsValid = input.QualityIsValid,
                qualitySummary = input.QualitySummary,
                decisionMode,
                resultsJson = input.ResultsJson,
                name = firstName,
                major = firstMajor
            };

            var requestUrl = input.AppsScriptWebhookUrl;
            HttpResponseMessage? response;
            var redirectHistory = new List<string>();

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            output.WebhookStatusCode = (int)response.StatusCode;

            if (IsRedirect(response.StatusCode))
            {
                var location = response.Headers.Location;
                if (location != null)
                {
                    requestUrl = location.IsAbsoluteUri ? location.AbsoluteUri : new Uri(new Uri(requestUrl), location).AbsoluteUri;
                    redirectHistory.Add($"{(int)response.StatusCode} -> {requestUrl}");
                }

                if (requestUrl.Contains("accounts.google.com", StringComparison.OrdinalIgnoreCase))
                {
                    output.WriteStatus = "FAILED";
                    output.WebhookMode = "REDIRECT_AUTH_REQUIRED";
                    output.ErrorMessage = "Google Apps Script Web App requires Google Account authentication.";
                    output.ErrorDetails = $"The request was redirected to the Google accounts login page. Please deploy the Apps Script Web App with:\n- 'Execute as: Me'\n- 'Who has access: Anyone'\nRedirect History:\n{string.Join("\n", redirectHistory)}";
                    output.Summary = "FAILED: Authentication required (redirected to accounts.google.com)";
                    response.Dispose();
                    return output;
                }

                if (!input.WaitForWebhookResponse)
                {
                    output.WriteStatus = "POST_ACCEPTED";
                    output.WebhookMode = "FAST_ACK_REDIRECT";
                    output.Summary = BuildSummary(input, stats, "POST_ACCEPTED", decisionMode);
                    response.Dispose();
                    return output;
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                output.WriteStatus = "FAILED";
                output.ErrorMessage = $"Apps Script webhook returned {(int)response.StatusCode}";
                output.ErrorDetails = $"Status Code: {response.StatusCode}\nRedirect History:\n{string.Join("\n", redirectHistory)}";
                output.Summary = $"FAILED: Webhook returned {(int)response.StatusCode}";
                response.Dispose();
                return output;
            }

            if (!input.WaitForWebhookResponse)
            {
                output.WriteStatus = "POST_ACCEPTED";
                output.WebhookMode = "FAST_ACK_SUCCESS";
                output.Summary = BuildSummary(input, stats, "POST_ACCEPTED", decisionMode);
                response.Dispose();
                return output;
            }

            var body = await response.Content.ReadAsStringAsync(ct);

            // Parse response body if it contains JSON error
            if (!string.IsNullOrWhiteSpace(body) && body.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                output.WriteStatus = "FAILED";
                output.WebhookMode = "WAIT_RESPONSE";
                output.ErrorMessage = "Google Apps Script executed with errors.";
                output.ErrorDetails = $"Body: {body}\nRedirect History:\n{string.Join("\n", redirectHistory)}";
                output.Summary = "FAILED: Apps Script execution error";
                response.Dispose();
                return output;
            }

            output.WriteStatus = "POSTED";
            output.WebhookMode = "WAIT_RESPONSE";
            output.Summary = BuildSummary(input, stats, "POSTED", decisionMode);
            response.Dispose();
            return output;
        }
        catch (Exception ex)
        {
            output.WriteStatus = "FAILED";
            output.ErrorMessage = ex.Message;
            output.ErrorDetails = ex.ToString();
            output.Summary = $"FAILED: {ex.Message}";
            return output;
        }
    }

    private static ResultStats InspectResults(string resultsJson)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(resultsJson) ? "[]" : resultsJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new ResultStats();
        }

        var stats = new ResultStats { RowsPrepared = doc.RootElement.GetArrayLength() };
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var decision = ReadProperty(item, "Decision");
            if (decision.Equals("STRONG", StringComparison.OrdinalIgnoreCase))
            {
                stats.StrongCount++;
            }
            else if (decision.Equals("REVIEW", StringComparison.OrdinalIgnoreCase))
            {
                stats.ReviewCount++;
            }
            else if (decision.Equals("REJECT", StringComparison.OrdinalIgnoreCase))
            {
                stats.RejectCount++;
            }
        }

        return stats;
    }

    private static string ReadProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.GetRawText();
            }
        }

        return string.Empty;
    }

    private static string BuildSummary(
        WriteBackSheetResultsInput input,
        ResultStats stats,
        string writeStatus,
        string decisionMode)
    {
        var quality = input.QualityIsValid ? "quality passed" : "quality needs review";
        return $"{writeStatus}: prepared {stats.RowsPrepared} rows ({stats.StrongCount} strong, {stats.ReviewCount} review, {stats.RejectCount} reject), {quality}, decision mode {decisionMode}.";
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.Redirect
            || statusCode == HttpStatusCode.Found
            || statusCode == HttpStatusCode.SeeOther
            || statusCode == (HttpStatusCode)307
            || statusCode == (HttpStatusCode)308;
    }

    private sealed class ResultStats
    {
        public int RowsPrepared { get; set; }
        public int StrongCount { get; set; }
        public int ReviewCount { get; set; }
        public int RejectCount { get; set; }
    }
}
