using System.Security.Claims;
using AWE.Domain.Entities;
using AWE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Data;
using System.Data.Common;

namespace AWE.ApiGateway.Middlewares;

public class UserLazySyncMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly ILogger<UserLazySyncMiddleware> _logger;

    public UserLazySyncMiddleware(
        RequestDelegate next,
        IMemoryCache cache,
        ILogger<UserLazySyncMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _logger = logger;
    }

    // Middleware là Singleton, nên DbContext phải được inject qua tham số hàm InvokeAsync (Scoped)
    public async Task InvokeAsync(HttpContext context, ApplicationDbContext dbContext)
    {
        // 1. Chỉ xử lý nếu Request đã được xác thực thành công bởi Keycloak
        if (context.User.Identity?.IsAuthenticated == true)
        {
            // Keycloak lưu 'sub' (Subject ID) vào ClaimTypes.NameIdentifier
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!string.IsNullOrEmpty(userId))
            {
                // 2. Kiểm tra Cache xem User này đã đồng bộ trong 1 giờ qua chưa
                var cacheKey = $"UserSynced_{userId}";
                if (!_cache.TryGetValue(cacheKey, out _))
                {
                    var email = context.User.FindFirst(ClaimTypes.Email)?.Value ?? "";
                    var name = context.User.FindFirst("preferred_username")?.Value ??
                               context.User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown User";
                    await SyncUserAsync(
                        dbContext,
                        userId,
                        email,
                        name,
                        cacheKey,
                        context.RequestAborted);
                }
            }
        }

        // Cho phép Request đi tiếp vào Controller
        await _next(context);
    }

    private async Task SyncUserAsync(
        ApplicationDbContext dbContext,
        string userId,
        string email,
        string displayName,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        var connection = dbContext.Database.GetDbConnection();
        var lockAcquired = false;

        try
        {
            await SetUserSyncLockAsync(connection, userId, acquire: true, cancellationToken);
            lockAcquired = true;

            // Another request in this API instance may have completed while this one waited.
            if (_cache.TryGetValue(cacheKey, out _))
            {
                return;
            }

            var now = DateTime.UtcNow;
            var user = await dbContext.AppUsers.FindAsync([userId], cancellationToken);

            if (user is null)
            {
                dbContext.AppUsers.Add(new AppUser
                {
                    Id = userId,
                    Email = email,
                    DisplayName = displayName,
                    CreatedAt = now,
                    LastLoginAt = now
                });
            }
            else
            {
                user.Email = email;
                user.DisplayName = displayName;
                user.LastLoginAt = now;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            _cache.Set(cacheKey, true, TimeSpan.FromHours(1));
        }
        finally
        {
            if (lockAcquired && connection.State == ConnectionState.Open)
            {
                try
                {
                    await SetUserSyncLockAsync(connection, userId, acquire: false, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Unable to release lazy user sync lock for {UserId}. The lock will be released when the connection closes.",
                        userId);
                }
            }

            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static async Task SetUserSyncLockAsync(
        DbConnection connection,
        string userId,
        bool acquire,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = acquire
            ? "SELECT pg_advisory_lock(hashtextextended(@user_id, 0));"
            : "SELECT pg_advisory_unlock(hashtextextended(@user_id, 0));";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "user_id";
        parameter.Value = userId;
        command.Parameters.Add(parameter);

        await command.ExecuteScalarAsync(cancellationToken);
    }
}

// Class tiện ích để gọi middleware cho gọn
public static class UserLazySyncMiddlewareExtensions
{
    public static IApplicationBuilder UseUserLazySync(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<UserLazySyncMiddleware>();
    }
}

