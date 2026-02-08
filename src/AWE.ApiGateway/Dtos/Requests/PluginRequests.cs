namespace AWE.ApiGateway.Dtos.Requests;

public record CreatePluginPackageRequest(
    string UniqueName,
    string DisplayName,
    string? Description);

public class UploadPluginVersionRequest
{
    // form-data keys phải đúng để Postman gửi
    public string Version { get; set; } = default!;
    public string Bucket { get; set; } = default!;
    public string? ReleaseNotes { get; set; }
    public IFormFile File { get; set; } = default!;

    // optional: nếu bạn muốn gửi schema JSON bằng string
    public string? ConfigSchemaJson { get; set; }
}
