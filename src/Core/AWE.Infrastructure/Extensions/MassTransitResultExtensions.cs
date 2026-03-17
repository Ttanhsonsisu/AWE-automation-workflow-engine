using AWE.Shared.Primitives;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AWE.Infrastructure.Extensions;

public static class MassTransitResultExtensions
{
    public static async Task ProcessResultAsync<TMessage>(
        this ConsumeContext<TMessage> context,
        Result result,
        ILogger logger,
        string actionName) where TMessage : class
    {
        if (result.IsSuccess)
        {
            logger.LogInformation("✅ [SUCCESS] {ActionName} completed.", actionName);
            return;
        }

        var error = result.Error!;

        switch (error.Type)
        {
            // Lỗi nghiệp vụ -> Log Warning & ACK (Không retry)
            case ErrorType.Validation:
            case ErrorType.BusinessRule:
            case ErrorType.NotFound:
            case ErrorType.Conflict:
            case ErrorType.Forbidden:
                logger.LogWarning("⚠️ [SKIPPED] {ActionName}: {ErrorCode} - {ErrorMessage}",
                    actionName, error.Code, error.Message);
                break;

            // Lỗi hệ thống -> Log Error & THROW (Để Retry)
            case ErrorType.Unexpected:
            case ErrorType.Failure:
            default:
                logger.LogError("❌ [RETRY] {ActionName}: {ErrorCode} - {ErrorMessage}",
                    actionName, error.Code, error.Message);
                throw error.ToException();
        }
    }
}
