using AWE.Infrastructure.Persistence;
using AWE.Shared.Consts;
using AWE.Wokrer.Engine.Consumers;
using MassTransit;

namespace AWE.Wokrer.Engine.Definitions;

public class WakeUpConsumerDefinition : ConsumerDefinition<WakeUpConsumer>
{
    public WakeUpConsumerDefinition()
    {
        // MassTransit sẽ tự động lấy tên này làm tên Queue
        EndpointName = MessagingConstants.QueueCore;
    }

    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<WakeUpConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // 1. PERFORMANCE (Core logic rất nhẹ, chủ yếu điều phối nên để 20 là đẹp)
        endpointConfigurator.PrefetchCount = 20;
        consumerConfigurator.UseConcurrencyLimit(20);

        // 2. RESILIENCE
        // Lỗi gọi DB hoặc Lock -> Retry ngay lập tức 5 lần
        endpointConfigurator.UseMessageRetry(r => r.Immediate(5));

        // Bật Inbox/Outbox Pattern của EF Core để chống duplicate message (Idempotency)
        endpointConfigurator.UseEntityFrameworkOutbox<ApplicationDbContext>(context);

        // 3. TOPOLOGY
        if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rabbit)
        {
            // Set Quorum Queue cho an toàn tuyệt đối
            rabbit.SetQuorumQueue();

            // Bind vào Exchange của Workflow (Tùy chọn, vì thường Quartz gọi trực tiếp Queue)
            // Nếu dùng hàm mở rộng của bạn thì:
            // rabbit.ConfigureQuorumQueue(MessagingConstants.ExchangeWorkflow, "workflow.cmd.wakeup");
        }
    }
}
