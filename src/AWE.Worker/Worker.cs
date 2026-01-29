using AWE.Contracts.Messages;
using MassTransit;

namespace AWE.Worker;

public class JobExecutionConsumer : IConsumer<SubmitWorkflowCommand>
{
    private readonly ILogger<JobExecutionConsumer> _logger;

    public JobExecutionConsumer(ILogger<JobExecutionConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SubmitWorkflowCommand> context)
    {
        // test 
        _logger.LogInformation("⏳ [WORKER] GET JOB...");
        await Task.Delay(2000);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=================================================");
        Console.WriteLine($"🏗️  AWE.WORKER PROCESSING JOB");
        Console.WriteLine($"🆔  Job ID:   {context.Message.DefinitionId}");
        Console.WriteLine($"📋  Job Name: {context.Message.JobName}");
        Console.WriteLine($"📦  Payload:  {context.Message.InputData}");
        Console.WriteLine("=================================================\n");
        Console.ResetColor();

        _logger.LogInformation("✅ [WORKER] PROCESS DONE!");
    }
}
