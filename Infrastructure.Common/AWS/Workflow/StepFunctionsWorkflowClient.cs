using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using Application.Common.Workflow;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Infrastructure.Common.AWS.Workflow
{
    /// <summary>
    /// <see cref="IWorkflowClient"/> implementation on AWS Step Functions.
    /// Workflow names may be either full state-machine ARNs or plain names
    /// (resolved once via ListStateMachines and cached).
    /// </summary>
    public class StepFunctionsWorkflowClient : IWorkflowClient
    {
        private readonly IAmazonStepFunctions _stepFunctionsClient;
        private static readonly ConcurrentDictionary<string, string> _stateMachineArnCache = new();

        public StepFunctionsWorkflowClient(IAmazonStepFunctions stepFunctionsClient)
        {
            _stepFunctionsClient = stepFunctionsClient;
        }

        public async Task<string> StartAsync<TInput>(string workflowName, TInput input, string? executionName = null, CancellationToken cancellationToken = default)
        {
            var response = await _stepFunctionsClient.StartExecutionAsync(new StartExecutionRequest
            {
                StateMachineArn = await ResolveStateMachineArnAsync(workflowName, cancellationToken),
                Name = executionName,
                Input = JsonSerializer.Serialize(input)
            }, cancellationToken);

            return response.ExecutionArn;
        }

        public async Task<WorkflowExecution> GetExecutionAsync(string executionId, CancellationToken cancellationToken = default)
        {
            var response = await _stepFunctionsClient.DescribeExecutionAsync(new DescribeExecutionRequest
            {
                ExecutionArn = executionId
            }, cancellationToken);

            return new WorkflowExecution(
                response.ExecutionArn,
                response.Status?.Value ?? "UNKNOWN",
                response.StartDate,
                response.StopDate,
                response.Output);
        }

        public async Task StopAsync(string executionId, string? reason = null, CancellationToken cancellationToken = default)
        {
            await _stepFunctionsClient.StopExecutionAsync(new StopExecutionRequest
            {
                ExecutionArn = executionId,
                Cause = reason
            }, cancellationToken);
        }

        public async Task SendTaskSuccessAsync<TOutput>(string taskToken, TOutput output, CancellationToken cancellationToken = default)
        {
            await _stepFunctionsClient.SendTaskSuccessAsync(new SendTaskSuccessRequest
            {
                TaskToken = taskToken,
                Output = JsonSerializer.Serialize(output)
            }, cancellationToken);
        }

        public async Task SendTaskFailureAsync(string taskToken, string error, string? cause = null, CancellationToken cancellationToken = default)
        {
            await _stepFunctionsClient.SendTaskFailureAsync(new SendTaskFailureRequest
            {
                TaskToken = taskToken,
                Error = error,
                Cause = cause
            }, cancellationToken);
        }

        private async Task<string> ResolveStateMachineArnAsync(string workflowName, CancellationToken cancellationToken)
        {
            if (workflowName.StartsWith("arn:", StringComparison.Ordinal))
            {
                return workflowName;
            }

            if (_stateMachineArnCache.TryGetValue(workflowName, out var cachedArn))
            {
                return cachedArn;
            }

            string? nextToken = null;
            do
            {
                var response = await _stepFunctionsClient.ListStateMachinesAsync(new ListStateMachinesRequest
                {
                    NextToken = nextToken
                }, cancellationToken);

                var match = response.StateMachines.FirstOrDefault(s => s.Name == workflowName);
                if (match is not null)
                {
                    _stateMachineArnCache[workflowName] = match.StateMachineArn;
                    return match.StateMachineArn;
                }

                nextToken = response.NextToken;
            } while (!string.IsNullOrEmpty(nextToken));

            throw new InvalidOperationException($"No Step Functions state machine named '{workflowName}' was found.");
        }
    }
}
