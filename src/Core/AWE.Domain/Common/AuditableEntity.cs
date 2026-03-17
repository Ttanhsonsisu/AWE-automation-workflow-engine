namespace AWE.Domain.Common;

public abstract class AuditableEntity : Entity
{
    public DateTime CreatedAt { get; protected set; }
    public DateTime? LastUpdated { get; protected set; }

    protected AuditableEntity() : base()
    {
        CreatedAt = DateTime.UtcNow;
    }

    protected AuditableEntity(Guid id) : base(id)
    {
        CreatedAt = DateTime.UtcNow;
    }

    public void MarkAsUpdated()
    {
        LastUpdated = DateTime.UtcNow;
    }
}
