using System.Text;
using System.Text.Json;
using AWE.Application.Services;
using AWE.Infrastructure.ConfigOptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AWE.Infrastructure.Services;

public class TelegramNotificationService : ITelegramNotificationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TelegramNotificationService> _logger;
    private readonly IOptions<TelegramNotificationConfig> _options;

    private string _botToken;
    private string _chatId;


    // Đưa 2 thông tin này vào appsettings.json nhé! Ở đây mình hardcode để minh họa

    public TelegramNotificationService(HttpClient httpClient, ILogger<TelegramNotificationService> logger, IOptions<TelegramNotificationConfig> options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options;
        _botToken = options.Value.BotToken;
        _chatId = options.Value.ChatID;
    }



    public async Task SendAlertAsync(string message)
    {
        try
        {
            var url = $"https://api.telegram.org/bot{_botToken}/sendMessage";
            var payload = new
            {
                chat_id = _chatId,
                text = message,
                parse_mode = "HTML" // Cho phép dùng thẻ <b>, <i>, <code> để trang trí tin nhắn
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to send Telegram alert. Telegram API response: {Error}", error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while sending Telegram alert.");
        }
    }
}
