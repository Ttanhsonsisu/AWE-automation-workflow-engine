using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace AWE.Domain.Plugins;

public record PluginContext
(
    Guid InstanceId,
    Guid StepId, 
    JsonDocument Inputs,
    CancellationToken CancellationToken
);
