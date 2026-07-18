using Amazon.Lambda.Model;
using Amazon.Lambda;
using Amazon.SQS.Model;
using Amazon.SQS;
using Domain.Common.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Application.Common;
using Application.Common.Event;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Infrastructure.Common.Cache;
using Utils.Common.Utils;

namespace Infrastructure.Common.AWS.Eventbus
{
    public class SimpleQueueService : IQueueService
    {
        private readonly Dictionary<string, AwsEventBusQueueOptions> _awsEventBusQueues;

        private readonly ILogger<SimpleQueueService> _logger;

        private readonly IAmazonSQS _sqsClient;

        private readonly IAmazonLambda _lambdaClient;

        private readonly List<AwsTag> _defaultTags;

        private readonly ICache _cache;

        private readonly int _defaultVisibilityTimeoutInSeconds;

        public SimpleQueueService(IOptions<AwsInfraSettings> awsInfraSettings,
         IOptions<AwsTagsConfigSettings> tagConfigurationSettings
         , ILogger<SimpleQueueService> logger, IAmazonSQS sqsClient,
         IAmazonLambda lambdaClient, ICache cache)
        {
            _awsEventBusQueues = awsInfraSettings.Value.Queues;
            _logger = logger;
            _sqsClient = sqsClient;
            _defaultTags = tagConfigurationSettings.Value.DefaultTags;
            _lambdaClient = lambdaClient;
            _cache = cache;
            _defaultVisibilityTimeoutInSeconds = awsInfraSettings.Value.DefaultVisibilityTimeoutInSeconds;
        }

        public async Task<string> ScheduleMessageAsync(object message, TimeSpan delay, CancellationToken cancellationToken = default)
        {
            var messageType = message.GetType().Name;
            var queueSetting = GetQueueSetting(messageType);
            _logger.LogDebug("Queueing {MessageType} message.", messageType);
            return await PublishMessageAsync(queueSetting, delay.Seconds, message.Serialize(), messageType, cancellationToken);
        }

        public async Task<string> SendMessageAsync(object message, CancellationToken cancellationToken = default)
        {
            var messageType = message.GetType().Name;
            var queueSetting = GetQueueSetting(messageType);
            _logger.LogDebug("Queueing {MessageType} message.", messageType);
            return await PublishMessageAsync(queueSetting, default, message.Serialize(), messageType, cancellationToken);
        }

        public async Task<string> SendMessagesAsync(IEnumerable<object> messages, CancellationToken cancellationToken = default)
        {
            var messageIds = new List<string>();
            foreach (var message in messages)
            {
                messageIds.Add(await SendMessageAsync(message, cancellationToken));
            }
            return string.Join(',', messageIds);
        }

        private AwsEventBusQueueOptions GetQueueSetting(string messageType)
        {
            // 1. Explicit configuration keyed by message type.
            if (_awsEventBusQueues.TryGetValue(messageType, out var queueOptions))
            {
                return queueOptions;
            }

            // 2. Configuration whose QueueName matches the message-type convention
            //    (covers configs keyed by queue name, as the event bus uses).
            var conventionQueueName = $"{messageType.ToLowerInvariant()}_queue.fifo";
            var byQueueName = _awsEventBusQueues.Values.FirstOrDefault(q =>
                string.Equals(q.QueueName, conventionQueueName, StringComparison.OrdinalIgnoreCase));
            if (byQueueName is not null)
            {
                return byQueueName;
            }

            // 3. Sensible defaults with a convention-named queue, so unconfigured message
            //    types work out of the box instead of throwing.
            _logger.LogInformation("No queue configuration for {MessageType}; using defaults with queue {Queue}.",
                messageType, conventionQueueName);
            return Constants.DefaultAwsEventBusQueueOptions with { QueueName = conventionQueueName };
        }

        private async Task<string> GetQueueUrlAsync(AwsEventBusQueueOptions queueSetting, CancellationToken cancellationToken)
        {
            var queueUrl = await _cache.GetAsync<string>(queueSetting.QueueName);

            if (!string.IsNullOrWhiteSpace(queueUrl))
            {
                return queueUrl;
            }
            try
            {
                GetQueueUrlResponse response = await _sqsClient.GetQueueUrlAsync(new GetQueueUrlRequest
                {
                    QueueName = queueSetting.QueueName
                }, cancellationToken);

                if (response.HttpStatusCode == HttpStatusCode.OK)
                {
                    queueUrl = response.QueueUrl;
                    await _cache.SetAsync(queueSetting.QueueName, response.QueueUrl);
                }
                return queueUrl ?? string.Empty;
            }
            catch (QueueDoesNotExistException ex)
            {
                _logger.LogWarning(ex, "Queue : {QueueName} does not exists. Attempting to create queue.", queueSetting.QueueName);
                try
                {
                    queueUrl = await CreateQueueWithDlqAsync(queueSetting, cancellationToken);
                    return queueUrl ?? string.Empty;
                }
                catch (AmazonSQSException exception)
                {
                    _logger.LogError(exception, "Exception occurred while creating Queue : {QueueName}.", queueSetting.QueueName);
                    return string.Empty;
                }
            }
        }

        private async Task<string> CreateQueueWithDlqAsync(AwsEventBusQueueOptions queueSetting, CancellationToken cancellationToken = default)
        {
            try
            {
                var dlqAttributes = new Dictionary<string, string>();

                var dlqSetting = queueSetting.DlqSetting;
                if (dlqSetting is not null)
                {
                    dlqAttributes = CreateAttributes(dlqSetting, null);
                }

                var dlqResponse = await _sqsClient.CreateQueueAsync(new CreateQueueRequest
                {
                    QueueName = GetDLQName(queueSetting.QueueName),
                    Attributes = dlqAttributes
                }, cancellationToken);

                await AddDefaultTagsForQueueAsync(dlqResponse.QueueUrl, cancellationToken);

                var dlqArn = await GetQueueArnAsync(dlqResponse.QueueUrl, cancellationToken);

                var attributes = CreateAttributes(queueSetting, dlqArn);

                var queueResponse = await _sqsClient.CreateQueueAsync(new CreateQueueRequest
                {
                    QueueName = queueSetting.QueueName,
                    Attributes = attributes
                }, cancellationToken);

                var queueUrl = queueResponse.QueueUrl;

                await AddDefaultTagsForQueueAsync(queueUrl, cancellationToken);

                var queueArn = await GetQueueArnAsync(queueUrl, cancellationToken);

                if (!string.IsNullOrWhiteSpace(queueSetting.LambdaFunctionName))
                {
                    try
                    {
                        await _lambdaClient.CreateEventSourceMappingAsync(new CreateEventSourceMappingRequest
                        {
                            EventSourceArn = queueArn,
                            FunctionName = queueSetting.LambdaFunctionName,
                            Enabled = true,
                            BatchSize = queueSetting.MaxConcurrentCalls
                        }, cancellationToken);

                        _logger.LogDebug("Associated Lambda function {LambdaFunctionName} with queue {QueueArn}", queueSetting.LambdaFunctionName, queueArn);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to set up subscription for Lambda {LambdaFunctionName} in queue {QueueName}", queueSetting.LambdaFunctionName, queueSetting.QueueName);
                    }
                }

                return queueUrl;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Not able to add create Queue with name {QueueName}.Error Message {ErrorMessage}", queueSetting.QueueName, ex.Message);
                return string.Empty;
            }
        }

        private static string GetDLQName(string queueName)
        {
            return queueName + "_DLQ";
        }

        private async Task<string> GetQueueArnAsync(string queueUrl, CancellationToken cancellationToken = default)
        {
            return (await _sqsClient.GetQueueAttributesAsync(queueUrl, new List<string> { "QueueArn" }, cancellationToken))
                             .Attributes["QueueArn"];
        }

        private async Task AddDefaultTagsForQueueAsync(string queueUrl, CancellationToken cancellationToken = default)
        {
            try
            {
                var defaultTagsDictionary = _defaultTags.ToDictionary(t => t.Key, t => t.Value);
                _logger.LogInformation("defaultTag key = {Key} , value ={Value}", _defaultTags.FirstOrDefault()?.Key, _defaultTags.FirstOrDefault()?.Value);
                await _sqsClient.TagQueueAsync(new TagQueueRequest
                {
                    QueueUrl = queueUrl,
                    Tags = defaultTagsDictionary
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Not able to add Tags to Queue with queueUrl {QueueUrl}.Error Message {ErrorMessage}", queueUrl, ex.Message);
            }
        }

        private Dictionary<string, string> CreateAttributes(AwsEventBusQueueOptions queueSetting, string? dlqArn)
        {
            var attributes = new Dictionary<string, string>();

            if (queueSetting.MessageRetentionPeriodInSeconds > 0)
            {
                attributes.Add("MessageRetentionPeriod", queueSetting.MessageRetentionPeriodInSeconds.ToString());
            }

            if (queueSetting.MessageDelayInSeconds.HasValue)
            {
                attributes.Add("DelaySeconds", queueSetting.MessageDelayInSeconds.Value.ToString());
            }

            var visibilityTimeout = queueSetting.VisibilityTimeoutInSeconds.HasValue ?
                                    queueSetting.VisibilityTimeoutInSeconds.HasValue.ToString()
                                    : _defaultVisibilityTimeoutInSeconds.ToString();

            attributes.Add("VisibilityTimeout", visibilityTimeout);

            if (!string.IsNullOrWhiteSpace(dlqArn))
            {
                string? redrivePolicy = queueSetting.MaxRetryCount > 0 ?
                JsonSerializer.Serialize(new
                {
                    maxReceiveCount = queueSetting.MaxRetryCount,
                    deadLetterTargetArn = dlqArn
                }) : JsonSerializer.Serialize(new
                {
                    deadLetterTargetArn = dlqArn
                });
                attributes.Add("RedrivePolicy", redrivePolicy);
            }

            return attributes;
        }

        private async Task<string> PublishMessageAsync(AwsEventBusQueueOptions queueSetting, int? delaySeconds, string? messageBody, string messageType, CancellationToken cancellationToken)
        {
            var queueUrl = await GetQueueUrlAsync(queueSetting, cancellationToken);
            if (!string.IsNullOrEmpty(queueUrl))
            {
                var queueNameWithLambdaSubscription = await _cache.GetAsync<List<string>>(Constant.QueueNameWithLambdaSubscription);

                if ((queueNameWithLambdaSubscription is null
                     || (queueNameWithLambdaSubscription is { Count: > 0 }
                         && !queueNameWithLambdaSubscription.Contains(queueSetting.QueueName)))
                     && !string.IsNullOrWhiteSpace(queueSetting.LambdaFunctionName)
                     && await DoesLambdaFunctionExistAsync(queueSetting.LambdaFunctionName))
                {
                    try
                    {
                        await _lambdaClient.CreateEventSourceMappingAsync(new CreateEventSourceMappingRequest
                        {
                            EventSourceArn = await GetQueueArnAsync(queueUrl, cancellationToken),
                            FunctionName = queueSetting.LambdaFunctionName,
                            Enabled = true,
                            BatchSize = queueSetting.MaxConcurrentCalls
                        }, cancellationToken);


                        _logger.LogDebug("Associated Lambda function {LambdaFunctionName} with queue {QueueName}", queueSetting.LambdaFunctionName, queueSetting.QueueName);
                    }
                    catch (ResourceConflictException ex)
                    {
                        _logger.LogInformation(ex, "Event source mapping already exists. Lambda function {LambdaFunctionName} with queue {QueueName}"
                                , queueSetting.LambdaFunctionName, queueSetting.QueueName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to set up subscription for Lambda {LambdaFunctionName} in queue {QueueName}", queueSetting.LambdaFunctionName, queueSetting.QueueName);
                    }

                    if (queueNameWithLambdaSubscription is null)
                    {
                        queueNameWithLambdaSubscription = new List<string> { queueSetting.QueueName };
                    }
                    else
                    {
                        queueNameWithLambdaSubscription.Add(queueSetting.QueueName);
                    }

                    await _cache.SetAsync(Constant.QueueNameWithLambdaSubscription, queueNameWithLambdaSubscription);
                }

                var sendMessageRequest = new SendMessageRequest
                {
                    MessageBody = messageBody,
                    QueueUrl = queueUrl,
                    MessageAttributes = new Dictionary<string, MessageAttributeValue>
                    {
                        { "Subject", new MessageAttributeValue { StringValue = messageType, DataType = "String" } },
                    }
                };

                if (queueSetting.QueueName.EndsWith(".fifo", StringComparison.OrdinalIgnoreCase))
                {
                    // FIFO queues REQUIRE a MessageGroupId (grouping by message type keeps
                    // per-type ordering) and reject per-message DelaySeconds.
                    sendMessageRequest.MessageGroupId = messageType;
                    sendMessageRequest.MessageDeduplicationId = Guid.NewGuid().ToString("N");
                    if (delaySeconds is > 0)
                    {
                        _logger.LogWarning("Per-message delay is not supported on FIFO queue {Queue}; sending without delay.", queueSetting.QueueName);
                    }
                }
                else
                {
                    sendMessageRequest.DelaySeconds = delaySeconds ?? 0;
                }

                var response = await _sqsClient.SendMessageAsync(sendMessageRequest, cancellationToken);
                return response.HttpStatusCode == HttpStatusCode.OK ? response.MessageId : string.Empty;
            }
            else
            {
                _logger.LogWarning("Error has occurred while getting the queue url. Please see logs for more details.");
                return string.Empty;
            }
        }

        public async Task<bool> DoesLambdaFunctionExistAsync(string functionName)
        {
            try
            {
                var request = new GetFunctionRequest
                {
                    FunctionName = functionName
                };

                var response = await _lambdaClient.GetFunctionAsync(request);
                return response.Configuration != null;
            }
            catch
            {
                return false;
            }
        }
    }

}
