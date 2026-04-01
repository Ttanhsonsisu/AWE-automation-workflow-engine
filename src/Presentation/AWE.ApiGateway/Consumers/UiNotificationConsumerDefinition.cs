using MassTransit;

namespace AWE.ApiGateway.Consumers;

public class UiNotificationConsumerDefinition : ConsumerDefinition<UiNotificationConsumer>
{
    public UiNotificationConsumerDefinition()
    {
        // Đặt tên Queue dành riêng cho Gateway (Khác với queue của Engine nhé)
        EndpointName = "q.workflow.gateway.ui";
    }

    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<UiNotificationConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // Cấu hình tải trọng nhẹ nhàng cho UI (Không cần gánh tải nặng như Core Worker)
        endpointConfigurator.PrefetchCount = 20;
        consumerConfigurator.UseConcurrencyLimit(10);

        // Ghi chú: Vì lệnh `context.Publish` của Engine dùng cơ chế mặc định của MassTransit (Fanout/Topic theo Type)
        // Nên ta không cần cấu hình Bind tay phức tạp ở đây, MassTransit sẽ tự động định tuyến (Auto-route).
    }
}
