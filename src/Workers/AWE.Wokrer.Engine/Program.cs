using System.Text;
using AWE.Infrastructure;
using AWE.ServiceDefaults.Extensions;
using AWE.Wokrer.Engine.Consumers;
using AWE.WorkflowEngine;
using MassTransit;

Console.OutputEncoding = Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);

// Setup Service Defaults (Log, Metric, HealthCheck, OpenTelemetry)
builder.AddServiceDefaults();

// Setup Database 
builder.Services.AddAwePersistence(builder.Configuration);

builder.Services.AddWorkflowEngineService();
// Setup Messaging & Workers
builder.Services.AddAweMessaging(builder.Configuration, x =>
{
    /// <remarks>
    /// Scans the assembly containing <see cref="JobExecutionConsumer"/>
    /// and automatically registers:
    /// - All consumers
    /// - Their corresponding ConsumerDefinition (if exists)
    /// </remarks>
    x.AddConsumersFromNamespaceContaining<JobExecutionConsumer>();

    /// <summary>
    /// Use kebab-case for endpoint names.
    /// </summary>
    /// <example>
    /// JobExecutionConsumer -> job-execution-consumer
    /// </example>
    x.SetKebabCaseEndpointNameFormatter();

    // IMPORTANT
    /// <remarks>
    /// Do NOT configure retry, prefetch, or concurrency here.
    /// Those settings must live inside each ConsumerDefinition
    /// to keep infrastructure clean and decentralized.
    /// </remarks>
});


var host = builder.Build();

// Write log Worker
var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("AWE Worker is STARTING...");

host.Run();
