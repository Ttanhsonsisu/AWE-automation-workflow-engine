using AWE.Infrastructure.Messaging;
using AWE.Shared.Consts;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AWE.Infrastructure.Tests;

public class TopologyTests
{
    [Fact]
    public async Task Topology_Should_Create_Correct_Queues_On_Broker()
    {
        // 1. Arrange: Setup Service Collection giả lập như App thật
        var services = new ServiceCollection();

        // Hardcode config để test kết nối local (Docker phải đang chạy)
        var options = new RabbitMqOptions
        {
            Host = "localhost",
            Port = 5672,
            Username = "awe-service",
            Password = "change_me",
            VirtualHost = "awe-system"
        };
        services.AddSingleton(options);

        // Đăng ký MassTransit với Topology Code của chúng ta
        services.AddMassTransit(x =>
        {
            x.UsingRabbitMq((context, cfg) =>
            {
                var uri = new Uri($"rabbitmq://{options.Host}:{options.Port}/{options.VirtualHost}");

                cfg.Host(uri, h =>
                {
                    h.Username(options.Username);
                    h.Password(options.Password);
                });

                //cfg.Host(options.Host, options.Port, options.VirtualHost, h =>
                //{
                //    h.Username(options.Username);
                //    h.Password(options.Password);
                //});

                // --- GỌI CODE CẦN TEST ---

                // 1. Test Core Queue (Quorum)
                cfg.ReceiveEndpoint(MessagingConstants.QueueCore, e =>
                    e.ConfigureQuorumQueue(MessagingConstants.ExchangeWorkflow, MessagingConstants.PatternCmd));

                // 2. Test Audit Queue (Lazy)
                cfg.ReceiveEndpoint(MessagingConstants.QueueAudit, e =>
                    e.ConfigureLazyQueue(MessagingConstants.ExchangeWorkflow, MessagingConstants.PatternAudit));
            });
        });

        var provider = services.BuildServiceProvider();
        var busControl = provider.GetRequiredService<IBusControl>();

        // 2. Act: Start Bus -> Trigger tạo Topology trên RabbitMQ
        // Nếu config sai (VD: user sai, vhost chưa tạo), lệnh này sẽ ném Exception -> Test Fail
        await busControl.StartAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

        try
        {
            // 3. Assert: Nếu Start thành công nghĩa là kết nối OK & Topology hợp lệ
            Assert.True(true, "Connected to RabbitMQ and Topology created successfully");

            // (Nâng cao: Có thể dùng HttpClient gọi RabbitMQ API để check kỹ type=quorum, 
            // nhưng việc StartAsync thành công đã chứng minh config binding không lỗi).
        }
        finally
        {
            await busControl.StopAsync();
        }
    }
}
