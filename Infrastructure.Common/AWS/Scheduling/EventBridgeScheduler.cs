using Amazon.Scheduler;
using Amazon.Scheduler.Model;
using Application.Common.Scheduling;

namespace Infrastructure.Common.AWS.Scheduling
{
    /// <summary>
    /// <see cref="IScheduler"/> implementation on Amazon EventBridge Scheduler:
    /// serverless cron/rate/one-time schedules invoking Lambda, SQS, SNS and 270+
    /// other targets.
    /// </summary>
    public class EventBridgeScheduler : IScheduler
    {
        private readonly IAmazonScheduler _schedulerClient;

        public EventBridgeScheduler(IAmazonScheduler schedulerClient)
        {
            _schedulerClient = schedulerClient;
        }

        public async Task CreateOrUpdateScheduleAsync(string scheduleName, string scheduleExpression, string targetArn,
            string roleArn, string? payload = null, CancellationToken cancellationToken = default)
        {
            var target = new Target
            {
                Arn = targetArn,
                RoleArn = roleArn,
                Input = payload
            };

            try
            {
                await _schedulerClient.CreateScheduleAsync(new CreateScheduleRequest
                {
                    Name = scheduleName,
                    ScheduleExpression = scheduleExpression,
                    Target = target,
                    FlexibleTimeWindow = new FlexibleTimeWindow { Mode = FlexibleTimeWindowMode.OFF }
                }, cancellationToken);
            }
            catch (ConflictException)
            {
                // Schedule already exists - replace it.
                await _schedulerClient.UpdateScheduleAsync(new UpdateScheduleRequest
                {
                    Name = scheduleName,
                    ScheduleExpression = scheduleExpression,
                    Target = target,
                    FlexibleTimeWindow = new FlexibleTimeWindow { Mode = FlexibleTimeWindowMode.OFF }
                }, cancellationToken);
            }
        }

        public async Task DeleteScheduleAsync(string scheduleName, CancellationToken cancellationToken = default)
        {
            try
            {
                await _schedulerClient.DeleteScheduleAsync(new DeleteScheduleRequest { Name = scheduleName }, cancellationToken);
            }
            catch (ResourceNotFoundException)
            {
                // Deleting a non-existent schedule is a no-op.
            }
        }
    }
}
