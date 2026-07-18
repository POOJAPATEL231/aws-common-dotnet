namespace Application.Common.Workflow
{
    public record WorkflowExecution(string ExecutionId, string Status, DateTime? StartedAtUtc, DateTime? StoppedAtUtc, string? Output);

    /// <summary>
    /// Long-running workflow orchestration abstraction (implemented for AWS Step
    /// Functions by Infrastructure.Common.AWS.Workflow.StepFunctionsWorkflowClient).
    /// Inputs/outputs are JSON documents.
    /// </summary>
    public interface IWorkflowClient
    {
        /// <summary>Starts an execution of the named workflow; returns the execution id.</summary>
        Task<string> StartAsync<TInput>(string workflowName, TInput input, string? executionName = null, CancellationToken cancellationToken = default);

        /// <summary>Gets the current state of an execution.</summary>
        Task<WorkflowExecution> GetExecutionAsync(string executionId, CancellationToken cancellationToken = default);

        /// <summary>Stops a running execution.</summary>
        Task StopAsync(string executionId, string? reason = null, CancellationToken cancellationToken = default);

        /// <summary>Reports success for a callback-pattern task token.</summary>
        Task SendTaskSuccessAsync<TOutput>(string taskToken, TOutput output, CancellationToken cancellationToken = default);

        /// <summary>Reports failure for a callback-pattern task token.</summary>
        Task SendTaskFailureAsync(string taskToken, string error, string? cause = null, CancellationToken cancellationToken = default);
    }
}
