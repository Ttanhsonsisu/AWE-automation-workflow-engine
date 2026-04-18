namespace AWE.Sdk.v2.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class UiFieldAttribute : Attribute
{
    /// <summary>
    /// Loại Widget: textarea, select, code-editor, file-picker...
    /// </summary>
    public string? Widget { get; set; }

    /// <summary>
    /// Nhóm các field lại với nhau trên giao diện (Accordion/Tab)
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// Biểu thức hiển thị (Ví dụ: "Channels.Contains('Email')")
    /// </summary>
    public string? ShowIf { get; set; }

    public string? Label { get; set; } // Tên hiển thị trên form
    public string? DataSourceUrl { get; set; }
}
