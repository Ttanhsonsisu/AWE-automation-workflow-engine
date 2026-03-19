namespace AWE.Application.Services;

public interface ITelegramNotificationService
{
    Task SendAlertAsync(string message);
}
