using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Hosting;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace AWE.ServiceDefaults.Extensions;

public static class ResilienceExtensions
{
    public static IHostApplicationBuilder AddDefaultResilience(this IHostApplicationBuilder builder)
    {
        // ========== HTTP CLIENT RESILIENCE ==========
        builder.Services.AddResiliencePipeline("http-pipeline", pipeline =>
        {
            pipeline.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential
            })
            .AddTimeout(TimeSpan.FromSeconds(10))
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(15)
            });
        });

        // ========== DATABASE RESILIENCE ==========
        //builder.Services.AddResiliencePipeline("database-pipeline", pipeline =>
        //{
        //    pipeline.AddRetry(new RetryStrategyOptions
        //    {
        //        MaxRetryAttempts = 5,
        //        Delay = TimeSpan.FromSeconds(1),
        //        ShouldHandle = new PredicateBuilder()
        //            .Handle<SqlException>()
        //            .Handle<TimeoutException>()
        //    })
        //    .AddTimeout(TimeSpan.FromSeconds(30));
        //});

        // ========== MESSAGE BUS RESILIENCE ==========
        builder.Services.AddResiliencePipeline("messaging-pipeline", pipeline =>
        {
            pipeline.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 10,
                Delay = TimeSpan.FromSeconds(5),
                BackoffType = DelayBackoffType.Exponential
            })
            .AddTimeout(TimeSpan.FromSeconds(60));
        });

        return builder;
    }
}
