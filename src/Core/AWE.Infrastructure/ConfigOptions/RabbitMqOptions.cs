using System.ComponentModel.DataAnnotations;

namespace AWE.Infrastructure.ConfigOptions;

public class RabbitMqOptions
{
    [Required(ErrorMessage = "RabbitMQ Host is required")]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 5672;

    [Required]
    public string VirtualHost { get; set; } = "/";

    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    [Range(1, 1000)]
    public int PrefetchCount { get; set; } = 16;
}
