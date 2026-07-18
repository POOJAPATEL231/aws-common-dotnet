namespace Application.Common.Scheduling
{
    /// <summary>
    /// Scheduled-invocation abstraction (implemented for Amazon EventBridge Scheduler
    /// by Infrastructure.Common.AWS.Scheduling.EventBridgeScheduler).
    /// </summary>
    public interface IScheduler
    {
        /// <summary>
        /// Creates or replaces a schedule that delivers <paramref name="payload"/> (JSON)
        /// to the given target on the given cadence.
        /// </summary>
        /// <param name="scheduleName">Unique schedule name.</param>
        /// <param name="scheduleExpression">e.g. "rate(5 minutes)", "cron(0 12 * * ? *)" or "at(2026-01-01T00:00:00)".</param>
        /// <param name="targetArn">ARN of the target (Lambda function, SQS queue, SNS topic, ...).</param>
        /// <param name="roleArn">IAM role the scheduler assumes to invoke the target.</param>
        /// <param name="payload">JSON payload delivered to the target.</param>
        Task CreateOrUpdateScheduleAsync(string scheduleName, string scheduleExpression, string targetArn,
            string roleArn, string? payload = null, CancellationToken cancellationToken = default);

        /// <summary>Deletes a schedule; no-op when it does not exist.</summary>
        Task DeleteScheduleAsync(string scheduleName, CancellationToken cancellationToken = default);
    }
}
