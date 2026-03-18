using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace AWE.Domain.Entities;

public class WorkflowSchedule
{
    public Guid Id { get; set; }
    public Guid DefinitionId { get; set; } 
    public virtual WorkflowDefinition Definition { get; set; } = null!;
    [Required]
    [MaxLength(50)]
    public string CronExpression { get; set; } = null!; // VD: "0 * * * *" (Mỗi giờ)

    public DateTime? LastRunAt { get; set; }

    // ĐÂY LÀ CỘT QUAN TRỌNG NHẤT: Lưu sẵn thời điểm sẽ chạy ở tương lai
    // Cần đánh Index (Chỉ mục) cho cột này trong EF Core
    public DateTime? NextRunAt { get; set; }

    public bool IsActive { get; set; } = true;

    // Concurrency Token: Giúp chống đụng độ khi có nhiều Server cùng quét DB
    [ConcurrencyCheck]
    public Guid Version { get; set; } = Guid.NewGuid();
}
