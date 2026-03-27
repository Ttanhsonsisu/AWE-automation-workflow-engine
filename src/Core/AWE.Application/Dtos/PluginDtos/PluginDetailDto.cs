
using System.Text.Json;

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
