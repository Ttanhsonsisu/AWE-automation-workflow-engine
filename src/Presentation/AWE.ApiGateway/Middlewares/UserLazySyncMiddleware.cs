using System.Security.Claims;
using AWE.Domain.Entities;
using AWE.Infrastructure.Persistence;
using Microsoft.Extensions.Caching.Memory;
using System.Linq;

namespace AWE.ApiGateway.Middlewares;

public class UserLazySyncMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;

    public UserLazySyncMiddleware(RequestDelegate next, IMemoryCache cache)
    {
        _next = next;
        _cache = cache;
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
                    // 3. Nếu chưa có trong Cache -> Tiến hành đồng bộ DB
                    var email = context.User.FindFirst(ClaimTypes.Email)?.Value ?? "";

                    // Keycloak thường đẩy tên vào 'preferred_username' hoặc 'name'
                    var name = context.User.FindFirst("preferred_username")?.Value ??
                               context.User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown User";

                    // Tìm user trong Database
                    var user = await dbContext.AppUsers.FindAsync(userId);

                    if (user == null)
                    {
                        // Chưa có -> Tạo mới
                        user = new AppUser
                        {
                            Id = userId,
                            Email = email,
                            DisplayName = name,
                            CreatedAt = DateTime.UtcNow,
                            LastLoginAt = DateTime.UtcNow
                        };
                        dbContext.AppUsers.Add(user);
                    }
                    else
                    {
                        // Có rồi -> Cập nhật thông tin mới nhất nhỡ họ đổi trên Keycloak
                        user.Email = email;
                        user.DisplayName = name;
                        user.LastLoginAt = DateTime.UtcNow;
                    }

                    await dbContext.SaveChangesAsync();

                    // 4. Lưu cờ vào Cache trong 1 giờ để các Request sau không bị dính query DB
                    _cache.Set(cacheKey, true, TimeSpan.FromHours(1));
                }
            }
        }

        // Cho phép Request đi tiếp vào Controller
        await _next(context);
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

