using AWE.Infrastructure.Extensions;
using AWE.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;

namespace AWE.Infrastructure.Tests;

public class ConfigurationTests
{
    [Fact]
    public void GetOptions_Should_Throw_If_Config_Missing()
    {
        // Arrange: Tạo config rỗng (Mô phỏng quên set .env)
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>()) // Không có data
            .Build();

        // Act & Assert: Mong đợi ném lỗi InvalidOperationException
        var ex = Assert.Throws<InvalidOperationException>(() =>
            config.GetOptions<RabbitMqOptions>("RabbitMq"));

        // Kiểm tra thông báo lỗi phải chứa tên Section
        Assert.Contains("RabbitMq", ex.Message);
    }

    [Fact]
    public void GetOptions_Should_Return_Valid_Object_If_Config_Ok()
    {
        // Arrange: Tạo config đầy đủ
        var inMemorySettings = new Dictionary<string, string?> {
            {"RabbitMq:Host", "localhost"},
            {"RabbitMq:Username", "user"},
            {"RabbitMq:Password", "pass"},
            {"RabbitMq:VirtualHost", "/vhost"},
            {"RabbitMq:Port", "5672"},
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        // Act
        var options = config.GetOptions<RabbitMqOptions>("RabbitMq");

        // Assert
        Assert.NotNull(options);
        Assert.Equal("localhost", options.Host);
        Assert.Equal(5672, options.Port);
    }
}
