using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using AWE.Domain.Common;

namespace AWE.Domain.Entities;

public class SystemAuditLog : AuditableEntity
{
    public string? UserId { get; set; } 
    public string? UserName { get; set; }

    public string TableName { get; set; } = null!; 

    public string Action { get; set; } = null!; // Added, Modified, Deleted

    public string RecordId { get; set; } = null!; // ID của Workflow bị sửa

    [Column(TypeName = "jsonb")]
    public string? OldValues { get; set; } // Dữ liệu trước khi sửa (JSON)

    [Column(TypeName = "jsonb")]
    public string? NewValues { get; set; } // Dữ liệu sau khi sửa (JSON)
}
