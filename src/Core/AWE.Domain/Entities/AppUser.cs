using System.ComponentModel.DataAnnotations;

namespace AWE.Domain.Entities;

public class AppUser
{
    [Key]
    public string Id { get; set; } = null!;

    public string Email { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastLoginAt { get; set; } = DateTime.UtcNow;
}
