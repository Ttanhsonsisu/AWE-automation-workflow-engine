using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace AWE.Sdk.v2;

public abstract class WorkflowPluginBase<TInput, TOutput> : IWorkflowPlugin
    where TInput : class, new()
    where TOutput : class
{
    public Type InputType => typeof(TInput);
    public Type OutputType => typeof(TOutput);

    public abstract string Name { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public abstract string Category { get; }
    public abstract string Icon { get; }

    public async Task<PluginResult> ExecuteAsync(PluginContext context)
    {
        TInput input;
        try
        {
            input = JsonSerializer.Deserialize<TInput>(context.Payload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new TInput();
        }
        catch (Exception ex)
        {
            return PluginResult.Failure($"Lỗi Parse JSON đầu vào: {ex.Message}");
        }

        // Tự động kiểm tra tính hợp lệ (Validation)
        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(input);

        bool isValid = Validator.TryValidateObject(input, validationContext, validationResults, true);
        if (!isValid)
        {
            var errors = string.Join("; ", validationResults.Select(r => r.ErrorMessage));
            return PluginResult.Failure($"Lỗi dữ liệu đầu vào: {errors}");
        }

        try
        {
            TOutput output = await ExecuteLogicAsync(input, context.CancellationToken);
            return PluginResult.Success(output);
        }
        catch (Exception ex)
        {
            return PluginResult.Failure(ex.Message);
        }
    }

    public virtual Task<PluginResult> CompensateAsync(PluginContext context)
        => Task.FromResult(PluginResult.Success());

    // Phương thức duy nhất Dev cần tập trung xử lý
    protected abstract Task<TOutput> ExecuteLogicAsync(TInput input, CancellationToken ct);
}
