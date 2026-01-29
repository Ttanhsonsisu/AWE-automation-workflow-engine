using System;
using System.Collections.Generic;
using System.Text;
using AWE.Infrastructure.Extensions;
using AWE.Infrastructure.Messaging;
using AWE.Infrastructure.Persistence;
using AWE.Shared.Consts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AWE.Infrastructure;

public static class DependencyInjection
{

    public static IServiceCollection AddAwePersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("postgres");

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

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

                cfg.Host(uri, h =>
                {
                    h.Username(opts.Username);
                    h.Password(opts.Password);
                });

                cfg.PrefetchCount = opts.PrefetchCount;

                // Auto-configure endpoints for registered consumers
                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
