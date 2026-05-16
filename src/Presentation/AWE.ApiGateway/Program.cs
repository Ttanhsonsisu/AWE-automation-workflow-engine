using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using AWE.ApiGateway.Consumers;
using AWE.ApiGateway.Middlewares;
using AWE.ApiGateway.Services;
using AWE.Application;
using AWE.Contracts.Messages;
using AWE.Infrastructure;
using AWE.Infrastructure.Persistence;
using AWE.ServiceDefaults.Extensions;
using AWE.Shared.Consts;
using AWE.WorkflowEngine;
using AWE.WorkflowEngine.Services;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.HttpOverrides;

Console.OutputEncoding = Encoding.UTF8;

// ================================================================
// Application bootstrap
// Responsibility:
// - Configure infrastructure services,...
// ================================================================
var builder = WebApplication.CreateBuilder(args);

// Register common service defaults (logging, health checks, etc.)
builder.AddServiceDefaults();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddMemoryCache();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("WebhookIngress", limiterOptions =>
    {
        limiterOptions.PermitLimit = 60;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 20;
    });
});

builder.Services.AddScoped<IWebhookSignatureVerificationStrategy, GithubWebhookSignatureVerificationStrategy>();
builder.Services.AddScoped<IWebhookSignatureVerificationStrategy, StripeWebhookSignatureVerificationStrategy>();
builder.Services.AddScoped<IWebhookSignatureVerificationStrategy, GenericWebhookSignatureVerificationStrategy>();
builder.Services.AddScoped<IWebhookSignatureVerifier, WebhookSignatureVerifier>();
builder.Services.AddScoped<IWebhookIngressService, WebhookIngressService>();
// config jwt authentication (adjust as needed for your auth setup)

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keycloak Authority — reads from config so it works in any environment.
        // Set via appsettings.json "Keycloak:Authority" or environment variable
        // Keycloak__Authority (Docker Compose / .env).
        // Default falls back to localhost for local development (npm run / dotnet run).
        options.Authority = builder.Configuration["Keycloak:Authority"]
            ?? "http://localhost:8081/realms/awe-auth/";
        options.RequireHttpsMetadata = false; // Disable for HTTP self-host (set true when HTTPS is configured)

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false, // Môi trường Dev có thể bỏ qua check Audience
            NameClaimType = "preferred_username",

            ValidateIssuer = true,
            ValidIssuers = new[]
            {
                builder.Configuration["Keycloak:Authority"],   // http://awe-keycloak:8080...
                builder.Configuration["Keycloak:ValidIssuer"]  // file .env
            }
        };

        // 2. BÍ KÍP MAP ROLE TỪ KEYCLOAK SANG CHUẨN .NET
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is ClaimsIdentity identity)
                {
                    // Đọc field realm_access trong Token của Keycloak
                    var realmAccessClaim = identity.FindFirst("realm_access")?.Value;
                    if (!string.IsNullOrEmpty(realmAccessClaim))
                    {
                        var realmAccess = JsonDocument.Parse(realmAccessClaim);
                        if (realmAccess.RootElement.TryGetProperty("roles", out var rolesElement))
                        {
                            // Đẩy từng role vào Claims của .NET
                            foreach (var role in rolesElement.EnumerateArray())
                            {
                                identity.AddClaim(new Claim(ClaimTypes.Role, role.GetString()!));
                            }
                        }
                    }
                }
                return Task.CompletedTask;
            }
        };
    });


builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AppPolicies.RequireAdmin, policy =>
        policy.RequireRole(AppRoles.SystemAdmin));

    options.AddPolicy(AppPolicies.RequireEditor, policy =>
        policy.RequireRole(AppRoles.SystemAdmin, AppRoles.WorkflowEditor));

    options.AddPolicy(AppPolicies.RequireOperator, policy =>
        policy.RequireRole(AppRoles.Operator, AppRoles.SystemAdmin));
});

// ------------------------------------------------------------
// Infrastructure configuration
// ------------------------------------------------------------

// Register persistence layer (EF Core, database context)
builder.Services.AddAwePersistence(builder.Configuration);

// Register messaging infrastructure 
builder.Services.AddAweMessaging(builder.Configuration, massTransit =>
{
    massTransit.AddConsumersFromNamespaceContaining<UiNotificationConsumer>();
    massTransit.AddRequestClient<SubmitWorkflowCommand>();
});

// add service engine
builder.Services.AddWorkflowEngineService();

// Register application layer services
builder.Services.AddAweApplication();

// Register object storage
//builder.Services.AddAweObjectStorage(builder.Configuration);
// config cors to allow frontend to call API (adjust as needed for production)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.SetIsOriginAllowed(_ => true)  // Cho phép tất cả các nguồn
              .AllowAnyHeader()   // Cho phép bất kỳ header nào
              .AllowAnyMethod()
              .AllowCredentials());  // Cho phép tất cả các phương thức HTTP
});

// Register SignalR for real-time updates (optional, adjust as needed)
builder.Services.AddSignalR();


// ------------------------------------------------------------
// Web API configuration
// ------------------------------------------------------------

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Encoder =
            System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});

// Register OpenAPI for development and testing
builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();

    try
    {
        logger.LogInformation(">> Đang kiểm tra và chạy Database Migrations...");

        // Lấy DbContext của bạn (Thay 'AweDbContext' bằng tên class thật của bạn)
        var dbContext = services.GetRequiredService<ApplicationDbContext>();

        // Lệnh này sẽ tự động tạo bảng nếu chưa có, hoặc chạy các script cập nhật mới
        dbContext.Database.Migrate();

        logger.LogInformation(">> Database Migrations hoàn tất thành công!");
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, ">> LỖI NGHIÊM TRỌNG: Không thể chạy Migration cho Database.");
        // Bắn lỗi ra ngoài để Container sập. 
        // Docker Compose (với restart: unless-stopped) sẽ tự động khởi động lại container sau vài giây để thử lại.
        throw;
    }
}

// Map default infrastructure endpoints (health, metrics, etc.)
app.MapDefaultEndpoints();


// ------------------------------------------------------------
// HTTP request pipeline
// ------------------------------------------------------------

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowAll");
app.UseRateLimiter();

app.UseAuthentication();

app.UseUserLazySync();

app.UseAuthorization();

app.MapControllers();
app.MapHub<WorkflowHub>("/hubs/workflow");

app.Run();
