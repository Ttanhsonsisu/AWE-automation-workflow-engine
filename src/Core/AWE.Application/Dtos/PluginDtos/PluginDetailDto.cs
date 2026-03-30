
using System.Text.Json;
using RabbitMQ.Client;

namespace AWE.Application.Dtos.PluginDtos;

public record PluginDetailDto
(
    string Name,
    string DisplayName,
    string ExecutionMode,
    string? Version,
    JsonElement? ExecutionMetadata,
    JsonElement InputSchema,
    JsonElement OutputSchema
);

public record PluginDetailDtoSha256
(
    Guid PackageId,
    string Name,
    string DisplayName,
    string Description,
    string Category,
    string Icon,
    string ExecutionMode,
    string? Version,
    JsonElement? ExecutionMetadata,
    JsonElement InputSchema,
    JsonElement OutputSchema
);

