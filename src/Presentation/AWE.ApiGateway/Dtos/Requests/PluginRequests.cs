using AWE.Domain.Enums;

namespace AWE.ApiGateway.Dtos.Requests;

public record CreatePluginPackageRequest(
    string UniqueName,
    string DisplayName,
    PluginExecutionMode ExecutionMode,
    string? Category,
    string? Icon,
    string? Description);

public class UploadPluginVersionRequest
{
    // form-data keys phải đúng để Postman gửi
    public string Version { get; set; } = default!;
    public string Bucket { get; set; } = "awe-plugins";
    public string? ReleaseNotes { get; set; }
    public IFormFile File { get; set; } = default!;
}
