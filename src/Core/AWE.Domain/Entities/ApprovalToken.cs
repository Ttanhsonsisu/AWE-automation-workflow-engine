namespace AWE.Domain.Entities;

public class ApprovalToken
{
    public Guid Id { get; set; }
    public Guid PointerId { get; set; }
    public string TokenString { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime ExpiredAt { get; set; }
    public bool IsUsed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
