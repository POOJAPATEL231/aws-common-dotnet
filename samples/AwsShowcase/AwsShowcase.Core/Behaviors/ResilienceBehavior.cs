using AwsShowcase.Core.Caching;
using MediatR;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace AwsShowcase.Core.Behaviors;

/// <summary>
/// Polly-based retry with exponential backoff for requests implementing
/// <see cref="IRetryableRequest"/> - absorbs transient faults (throttling,
/// optimistic-concurrency conflicts) without leaking retries into handlers.
/// </summary>
public class ResilienceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<ResilienceBehavior<TRequest, TResponse>> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;

    public ResilienceBehavior(ILogger<ResilienceBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
        _retryPolicy = Policy
            .Handle<Exception>(IsTransient)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1)),
                onRetry: (exception, delay, attempt, _) =>
                    _logger.LogWarning(exception, "Transient failure in {Request}; retry {Attempt} after {Delay}.",
                        typeof(TRequest).Name, attempt, delay));
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IRetryableRequest)
        {
            return await next();
        }

        return await _retryPolicy.ExecuteAsync(() => next());
    }

    private static bool IsTransient(Exception exception) =>
        exception is Persistence.Common.AWS.DynamoDbConcurrencyException
        || exception is Amazon.DynamoDBv2.Model.ProvisionedThroughputExceededException
        || exception is Amazon.DynamoDBv2.Model.InternalServerErrorException
        || exception is TimeoutException;
}
