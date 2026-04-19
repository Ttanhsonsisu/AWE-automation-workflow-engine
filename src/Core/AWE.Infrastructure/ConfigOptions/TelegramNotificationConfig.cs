using System;
using System.Collections.Generic;
using System.Text;

namespace AWE.Infrastructure.ConfigOptions;

public class TelegramNotificationConfig
{
    public string BotToken { get; set; } = string.Empty;
    public string ChatID { get; set; } = string.Empty;
}
