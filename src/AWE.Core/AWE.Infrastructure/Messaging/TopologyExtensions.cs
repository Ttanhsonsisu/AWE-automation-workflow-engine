using System;
using System.Collections.Generic;
using System.Text;
using MassTransit;
using RabbitMQ.Client;

namespace AWE.Infrastructure.Messaging;

public static class TopologyExtensions
{
    /// <summary>
    /// config Queue safe cho Core Engine (Quorum)
    /// </summary>
    public static void ConfigureQuorumQueue(this IRabbitMqReceiveEndpointConfigurator e, string exchange, string routingKey)
    {
        e.SetQuorumQueue();
        e.QueueExpiration = null;

        // Explicit DLQ (Dead Letter Queue)
        e.SetQueueArgument("x-dead-letter-exchange", "");
        e.SetQueueArgument("x-dead-letter-routing-key", $"{e.InputAddress.AbsolutePath.Trim('/')}_error");

        e.Bind(exchange, x =>
        {
            x.RoutingKey = routingKey;
            x.ExchangeType = ExchangeType.Topic;
            x.Durable = true;
            x.AutoDelete = false;
        });
    }

    // config CLASSIC (high speed)
    public static void ConfigureClassicQueue(this IRabbitMqReceiveEndpointConfigurator e, string exchange, string routingKey)
    {
        e.SetQueueArgument("x-queue-type", "classic");
        e.Bind(exchange, x => { x.RoutingKey = routingKey; x.ExchangeType = ExchangeType.Topic; });
    }

    // config LAZY (save RAM for Log)
    public static void ConfigureLazyQueue(this IRabbitMqReceiveEndpointConfigurator e, string exchange, string routingKey)
    {
        e.SetQueueArgument("x-queue-type", "classic");
        e.SetQueueArgument("x-queue-mode", "lazy");
        e.Bind(exchange, x => { x.RoutingKey = routingKey; x.ExchangeType = ExchangeType.Topic; });
    }
}
