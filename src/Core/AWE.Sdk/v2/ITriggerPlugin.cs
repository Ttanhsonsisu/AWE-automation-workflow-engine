namespace AWE.Sdk.v2;

public interface ITriggerPlugin : IWorkflowPlugin
{
    string TriggerSource { get; }
    bool IsSingleton { get; }
}
