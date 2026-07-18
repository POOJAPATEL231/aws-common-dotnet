using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AwsShowcase.Core.Behaviors;

/// <summary>Logs every request with its duration; errors bubble with full context.</summary>
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next();
            _logger.LogInformation("{Request} handled in {ElapsedMs} ms", typeof(TRequest).Name, stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Request} failed after {ElapsedMs} ms", typeof(TRequest).Name, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
