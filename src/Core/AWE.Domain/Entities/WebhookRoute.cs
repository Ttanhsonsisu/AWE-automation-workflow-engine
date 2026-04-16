using AWE.Domain.Common;

namespace AWE.Domain.Entities;

public class WebhookRoute : AuditableEntity
{
    public string RoutePath { get; private set; } = string.Empty;
    public Guid WorkflowDefinitionId { get; private set; }

    // Thuật toán dùng plain token hoặc chuẩn bị cho HMAC
    public string? SecretToken { get; private set; }

    // Đường dẫn JSONPath để trích xuất Key chống trùng lặp. VD: "$.body.data.id" hoặc "$.header.x-github-delivery"
    public string? IdempotencyKeyPath { get; private set; }

    public bool IsActive { get; private set; }

    private WebhookRoute() : base() { } // Dành cho EF Core

    public WebhookRoute(string routePath, Guid workflowDefinitionId, string? secretToken, string? idempotencyKeyPath)
    {
        RoutePath = routePath;
        WorkflowDefinitionId = workflowDefinitionId;
        SecretToken = secretToken;
        IdempotencyKeyPath = idempotencyKeyPath;
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
        MarkAsUpdated();
    }

    public void UpdateRoute(Guid newDefinitionId, string? secretToken, string? idempotencyKeyPath)
    {
        WorkflowDefinitionId = newDefinitionId;
        SecretToken = secretToken;
        IdempotencyKeyPath = idempotencyKeyPath;
        IsActive = true;
        MarkAsUpdated();
    }
}
