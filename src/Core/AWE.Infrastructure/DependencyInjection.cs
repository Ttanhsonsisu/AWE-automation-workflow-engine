using AWE.Application.Abstractions.Persistence;
using AWE.Application.Abstractions.Validation;
using AWE.Application.Services;
using AWE.Infrastructure.ConfigOptions;
using AWE.Infrastructure.Extensions;
using AWE.Infrastructure.Persistence;
using AWE.Infrastructure.Persistence.Interceptors;
using AWE.Infrastructure.Persistence.Repositories;
using AWE.Infrastructure.Plugins;
using AWE.Infrastructure.Services;
using AWE.Infrastructure.Validation;
using AWE.Shared.Consts;
using MassTransit;
using Medallion.Threading;
using Medallion.Threading.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using Quartz;
using StackExchange.Redis;

namespace AWE.Infrastructure;

/// <summary>
/// Extension methods for registering infrastructure services
/// </summary>
public static class DependencyInjection
{

    public static IServiceCollection AddAwePersistence(this IServiceCollection services, IConfiguration configuration)
    {
        // add redis
        services.AddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis") ?? "localhost:6379"));

        services.AddSingleton<IDistributedLockProvider>(sp =>
            new RedisDistributedSynchronizationProvider(sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase()));

        var connectionString = configuration.GetConnectionString("postgres")
            ?? throw new InvalidOperationException("Connection string not found");

        // add telegram notification service
        services.Configure<TelegramNotificationConfig>(configuration.GetSection("TelegramNotificationConfig"));
        var minioSettings = configuration.GetSection("TelegramNotificationConfig").Get<TelegramNotificationConfig>()
            ?? new TelegramNotificationConfig
            {
                BotToken = "your_bot_token",
                ChatID = "your_chat_id"
            };

        // add audit interceptor
        services.AddSingleton<AuditSaveChangesInterceptor>();

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            var interceptor = sp.GetRequiredService<AuditSaveChangesInterceptor>();

            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
                npgsqlOptions.CommandTimeout(30);
            })
            // Add the interceptor to the DbContext
            .AddInterceptors(interceptor);

            // Enable sensitive data logging in dev 
            if (configuration.GetValue<bool>("DetailedErrors"))
            {
                options.EnableSensitiveDataLogging(false);
                options.EnableDetailedErrors();
            }

        });

        // Repository
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IWorkflowDefinitionRepository, WorkflowDefinitionRepository>();
        services.AddScoped<IWorkflowInstanceRepository, WorkflowInstanceRepository>();
        services.AddScoped<IJoinBarrierRepository, JoinBarrierRepository>();
        services.AddScoped<IExecutionLogRepository, ExecutionLogRepository>();
        services.AddScoped<IExecutionPointerRepository, ExecutionPointerRepository>();
        services.AddScoped<IPluginPackageRepository, PluginPackageRepository>();
        services.AddScoped<IPluginVersionRepository, PluginVersionRepository>();
        services.AddScoped<IWorkflowScheduleRepository, WorkflowScheduleRepository>();
        services.AddScoped<ISystemAuditLogRepository, SystemAuditLogRepository>();

        // Service
        services.AddAweObjectStorage(configuration);
        services.AddSingleton<PluginLoader>();
        services.AddScoped<IStorageService, MinioStorageService>();
        services.AddScoped<IPluginValidator, PluginValidator>();
        services.AddScoped<IPluginService, PluginService>();
        services.AddScoped<ITelegramNotificationService, TelegramNotificationService>();
        // Background service
        // TODO:

        return services;
    }

    public static IServiceCollection AddAweMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configureConsumers = null)
    {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(MessagingConstants.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Get & Validate Options
        var opts = configuration.GetOptions<RabbitMqOptions>(MessagingConstants.SectionName);
        services.AddSingleton(opts);

        // Setup MassTransit
        services.AddMassTransit(x =>
        {
            // Allow upper layers to register consumers
            configureConsumers?.Invoke(x);

            x.SetKebabCaseEndpointNameFormatter();

            // add quartz scheduler
            x.AddQuartzConsumers();
            x.AddMessageScheduler(new Uri($"queue:{MessagingConstants.QueueQuartz}"));

            // add outbox config
            x.AddEntityFrameworkOutbox<ApplicationDbContext>(o =>
            {
                // config lock statement for Postgresql ( race condition )
                o.UsePostgres();
                // enable outbox for bus (publish message in db first)
                o.UseBusOutbox();
                // set auto delete message after send success 
                o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
            });

            x.UsingRabbitMq((context, cfg) =>
            {
                // URI Connection String
                var uri = new Uri($"rabbitmq://{opts.Host}:{opts.Port}/{opts.VirtualHost}");

                cfg.UseMessageScheduler(new Uri($"queue:{MessagingConstants.QueueQuartz}"));

                cfg.Host(uri, h =>
                {
                    h.Username(opts.Username);
                    h.Password(opts.Password);
                });

                cfg.ReceiveEndpoint(MessagingConstants.QueueQuartz, e =>
                {
                    // CHỈ bind các Consumer của chuẩn Quartz của MassTransit vào đây
                    //e.ConfigureConsumers(context);
                    e.ConfigureConsumer<MassTransit.QuartzIntegration.ScheduleMessageConsumer>(context);
                    e.ConfigureConsumer<MassTransit.QuartzIntegration.CancelScheduledMessageConsumer>(context);
                    e.ConfigureConsumer<MassTransit.QuartzIntegration.PauseScheduledMessageConsumer>(context);
                    e.ConfigureConsumer<MassTransit.QuartzIntegration.ResumeScheduledMessageConsumer>(context);
                });

                cfg.PrefetchCount = opts.PrefetchCount;

                // Auto-configure endpoints for registered consumers
                cfg.ConfigureEndpoints(context);
            });
        });

        // Quartz.NET configuration
        services.AddQuartz(q =>
        {
            q.SetProperty("quartz.serializer.type", "json");

            // Cấu hình Persistent Store
            q.UsePersistentStore(s =>
            {
                // 1. Chỉ định dùng PostgreSQL
                s.UsePostgres(postgresOptions =>
                {
                    postgresOptions.ConnectionString = configuration.GetConnectionString("postgres")
                                                        ?? throw new InvalidOperationException("Connection string not found");
                    postgresOptions.TablePrefix = "qrtz_"; // Tiền tố chuẩn của bảng Quartz
                });

                s.UseSystemTextJsonSerializer();

                // 3. Bật chế độ Clustering cho kiến trúc Microservices/Nhiều Worker
                s.UseClustering(c =>
                {
                    c.CheckinMisfireThreshold = TimeSpan.FromSeconds(20);
                    c.CheckinInterval = TimeSpan.FromSeconds(10);
                });
            });
        });

        services.AddQuartzHostedService(options =>
        {
            options.WaitForJobsToComplete = true;
        });


        return services;
    }

    public static IServiceCollection AddAweObjectStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MinioOptions>(configuration.GetSection("Minio"));
        var minioSettings = configuration.GetSection("Minio").Get<MinioOptions>()
            ?? new MinioOptions
            {
                Endpoint = "localhost:9100",
                AccessKey = "awe-service",
                SecretKey = "change_me",
                BucketName = "awe-plugins",
                UseSSL = false,
                Region = "us-east-1"
            };

        // register Minio SDK Client
        services.AddMinio(config => config
            .WithEndpoint(minioSettings.Endpoint)
            .WithCredentials(minioSettings.AccessKey, minioSettings.SecretKey)
            .WithSSL(minioSettings.UseSSL)
        );

        services.AddScoped<IStorageService, MinioStorageService>();

        return services;
    }
    public static async Task InitializeDatabaseAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await context.Database.MigrateAsync();
    }
}
