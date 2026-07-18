using Amazon.Lambda;
using Amazon.Lambda.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Domain.Common.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Infrastructure.Common.AWS.Eventbus
{
    public class AwsEventBusPersisterConnection : IEventBusPersisterConnection
    {
        private readonly IAmazonSQS _sqsClient;
        private readonly IAmazonSimpleNotificationService _snsClient;
        private readonly IAmazonLambda _lambdaClient;
        private readonly ILogger<AwsEventBusPersisterConnection> _logger;
        private readonly ConcurrentDictionary<string, string> _topicArn = new();
        private readonly Dictionary<string, AwsEventBusQueueOptions> _eventBusOptions;
        private readonly List<AwsTag> _defaultTags;

        private readonly string _eventDispatcherFunctionName;

        public AwsEventBusPersisterConnection(IAmazonSQS sqsClient, IAmazonSimpleNotificationService snsClient,
         IAmazonLambda lambdaClient, ILogger<AwsEventBusPersisterConnection> logger,
         IOptions<AwsInfraSettings> awsInfraSettings,
         IOptions<AwsTagsConfigSettings> tagConfigurationSettings)
        {
            _sqsClient = sqsClient ?? throw new ArgumentNullException(nameof(sqsClient));
            _snsClient = snsClient ?? throw new ArgumentNullException(nameof(snsClient));
            _lambdaClient = lambdaClient ?? throw new ArgumentNullException(nameof(lambdaClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _eventBusOptions = awsInfraSettings.Value.Queues;
            _defaultTags = tagConfigurationSettings.Value.DefaultTags;
            _eventDispatcherFunctionName = awsInfraSettings.Value.EventDispatcherFunctionName;
        }

        #region Publish

        public async Task PublishAsync(string message, string eventName, CancellationToken cancellationToken = default)
        {
            try
            {
                var topicName = GetTopicName(eventName) ?? eventName;

                var topicArn = await GetOrCreateTopicWithSetupAsync(topicName, cancellationToken);

                var publishRequest = new PublishRequest
                {
                    TopicArn = topicArn,
                    Message = message,
                    MessageAttributes = new Dictionary<string, Amazon.SimpleNotificationService.Model.MessageAttributeValue>
                    {
                        { "Subject", new Amazon.SimpleNotificationService.Model.MessageAttributeValue { StringValue = eventName, DataType = "String" } }
                    },
                    MessageGroupId = Guid.NewGuid().ToString(),
                    MessageDeduplicationId = Guid.NewGuid().ToString()
                };

                var response = await _snsClient.PublishAsync(publishRequest, cancellationToken);
                _logger.LogDebug("Published message to SNS topic {TopicArn} with message ID {MessageId}", topicArn, response.MessageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish message to topic With event Name {EventName}", eventName);
            }
        }

        #endregion

        #region Subscribe

        public async Task<string> GetOrCreateTopicWithSetupAsync(string topicName, CancellationToken cancellationToken = default)
        {
            var existingTopicArn = await GetTopicArnIfExistsAsync(topicName, cancellationToken);
            if (existingTopicArn != null)
            {
                _logger.LogDebug("Topic {TopicName} already exists with ARN {TopicArn}", topicName, existingTopicArn);
                return existingTopicArn;
            }

            string topicArn;
            try
            {
                var topicResponse = await _snsClient.CreateTopicAsync(new CreateTopicRequest
                {
                    Name = $"{topicName}.fifo",
                    Attributes = new Dictionary<string, string>
                    {
                        { "FifoTopic", "true" } // Declare this as a FIFO topic
                    }
                }, cancellationToken);
                topicArn = topicResponse.TopicArn;
                await AddDefaultTagsToSnsTopicAsync(topicArn, cancellationToken);
                _topicArn[$"{topicName}.fifo"] = topicArn;
                _logger.LogDebug("Created new topic {TopicName} with ARN {TopicArn}", topicName, topicArn);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create SNS topic {TopicName}", topicName);
                return string.Empty;
            }

            try
            {
                var queueName = $"{topicName}_Queue.fifo";
                var dlqName = $"{topicName}_Queue_DLQ.fifo";
                await CreateQueueWithDlqAndSubscribeAsync(topicArn, queueName, dlqName, cancellationToken);

                if (!string.IsNullOrEmpty(_eventDispatcherFunctionName))
                {
                    _eventBusOptions.TryGetValue(queueName, out var queueSetting);
                    queueSetting ??= Constants.DefaultAwsEventBusQueueOptions;
                    await AssociateLambdaWithQueueAsync(queueName, queueSetting?.MaxConcurrentCalls ?? 10, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set up subscription and Lambda for topic {TopicName}", topicName);
            }

            return topicArn;
        }

        public async Task<string> CreateQueueWithDlqAndSubscribeAsync(string topicArn, string queueName, string dlqName, CancellationToken cancellationToken = default)
        {
            _eventBusOptions.TryGetValue(queueName, out var queueSetting);
            queueSetting ??= Constants.DefaultAwsEventBusQueueOptions;

            var dlqAttributes = new Dictionary<string, string>();

            if (queueSetting is not null)
            {
                var dlqSetting = queueSetting.DlqSetting;
                if (dlqSetting is not null)
                {
                    dlqAttributes = CreateAttributes(dlqSetting, null);
                }
            }

            var dlqResponse = await _sqsClient.CreateQueueAsync(new CreateQueueRequest
            {
                QueueName = dlqName,
                Attributes = dlqAttributes
            }, cancellationToken);

            await AddDefaultTagsForQueueAsync(dlqResponse.QueueUrl, cancellationToken);

            var dlqArn = await GetQueueArnAsync(dlqResponse.QueueUrl, cancellationToken);

            var attributes = new Dictionary<string, string>();
            if (queueSetting is not null)
            {
                attributes = CreateAttributes(queueSetting, dlqArn);
            }

            var queueResponse = await _sqsClient.CreateQueueAsync(new CreateQueueRequest
            {
                QueueName = queueName,
                Attributes = attributes
            }, cancellationToken);

            var queueUrl = queueResponse.QueueUrl;

            await AddDefaultTagsForQueueAsync(queueUrl, cancellationToken);

            var queueArn = await GetQueueArnAsync(queueUrl, cancellationToken);

            var policy = CreateQueuePolicy(topicArn, queueArn);

            await _sqsClient.SetQueueAttributesAsync(new SetQueueAttributesRequest
            {
                QueueUrl = queueUrl,
                Attributes = new Dictionary<string, string> {
                            {
                                "Policy", policy
                            }}
            }, cancellationToken);

            var subscribeResponse = await _snsClient.SubscribeAsync(new SubscribeRequest
            {
                TopicArn = topicArn,
                Protocol = "sqs",
                Endpoint = queueArn
            }, cancellationToken);

            if (!string.IsNullOrEmpty(subscribeResponse.SubscriptionArn))
            {
                await _snsClient.SetSubscriptionAttributesAsync(new SetSubscriptionAttributesRequest
                {
                    SubscriptionArn = subscribeResponse.SubscriptionArn,
                    AttributeName = "RawMessageDelivery",
                    AttributeValue = "true"
                }, cancellationToken);
            }

            _logger.LogDebug("Subscribed SQS queue {QueueArn} to SNS topic {TopicArn}", queueArn, topicArn);

            return queueUrl;
        }

        private static Dictionary<string, string> CreateAttributes(AwsEventBusQueueOptions queueSetting, string? dlqArn)
        {
            var attributes = new Dictionary<string, string>
            {
                { "FifoQueue", "true" },
                { "ContentBasedDeduplication", "true" } //  enable automatic deduplication
            };

            if (queueSetting.MessageRetentionPeriodInSeconds > 0)
            {
                attributes.Add("MessageRetentionPeriod", queueSetting.MessageRetentionPeriodInSeconds.ToString());
            }

            if (queueSetting.MessageDelayInSeconds.HasValue)
            {
                attributes.Add("DelaySeconds", queueSetting.MessageDelayInSeconds.Value.ToString());
            }

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

        public async Task AssociateLambdaWithQueueAsync(string queueName, int maxConcurrentCalls, CancellationToken cancellationToken = default)
        {
            var queueUrl = (await _sqsClient.GetQueueUrlAsync(queueName, cancellationToken))?.QueueUrl;

            if (string.IsNullOrWhiteSpace(queueUrl))
            {
                return;
            }

            var queueArn = await GetQueueArnAsync(queueUrl, cancellationToken);

            await _lambdaClient.CreateEventSourceMappingAsync(new CreateEventSourceMappingRequest
            {
                EventSourceArn = queueArn,
                FunctionName = _eventDispatcherFunctionName,
                Enabled = true,
                BatchSize = maxConcurrentCalls
            }, cancellationToken);

            _logger.LogDebug("Associated Lambda function {LambdaFunctionName} with queue {QueueArn}", _eventDispatcherFunctionName, queueArn);
        }

        #endregion

        #region Unsubscribe

        public async Task UnsubscribeAsync(string topicName)
        {
            var topicArn = await GetTopicArnIfExistsAsync(topicName);
            if (string.IsNullOrEmpty(topicArn))
            {
                _logger.LogWarning("Topic {TopicName} does not exist, skipping deletion.", topicName);
                return;
            }

            await DeleteTopicSubscriptionsAsync(topicArn);

            var queueName = $"{topicName}_Queue.fifo";
            var dlqName = $"{topicName}_Queue_DLQ.fifo";
            await DeleteQueueAsync(queueName, true);
            await DeleteQueueAsync(dlqName, false);

            await _snsClient.DeleteTopicAsync(new DeleteTopicRequest { TopicArn = topicArn });
            _logger.LogDebug("Deleted topic {TopicName} with ARN {TopicArn}", topicName, topicArn);
        }

        private async Task DeleteTopicSubscriptionsAsync(string topicArn)
        {
            string nextToken = string.Empty;
            do
            {
                var response = await _snsClient.ListSubscriptionsByTopicAsync(new ListSubscriptionsByTopicRequest { TopicArn = topicArn, NextToken = nextToken });

                if (response is not null)
                {
                    foreach (var subscription in response.Subscriptions)
                    {
                        await _snsClient.UnsubscribeAsync(new UnsubscribeRequest { SubscriptionArn = subscription.SubscriptionArn });
                    }

                    nextToken = response.NextToken;
                }
            } while (!string.IsNullOrEmpty(nextToken));
        }

        private async Task DeleteQueueAsync(string queueName, bool callRemoveLambdaMap)
        {
            try
            {
                var queueUrl = (await _sqsClient.GetQueueUrlAsync(queueName))?.QueueUrl;

                if (!string.IsNullOrWhiteSpace(queueUrl))
                {
                    await _sqsClient.DeleteQueueAsync(queueUrl);
                    if (callRemoveLambdaMap)
                    {
                        await RemoveLambdaEventSourceMappingAsync(queueName);
                    }
                }
            }
            catch (QueueDoesNotExistException ex)
            {
                _logger.LogWarning(ex, "Queue {QueueName} does not exist, skipping deletion.", queueName);
            }
        }

        private async Task RemoveLambdaEventSourceMappingAsync(string queueName)
        {
            var queueUrl = (await _sqsClient.GetQueueUrlAsync(queueName))?.QueueUrl;

            if (string.IsNullOrWhiteSpace(queueUrl))
            {
                return;
            }

            var queueArn = await GetQueueArnAsync(queueUrl);

            var mappings = await _lambdaClient.ListEventSourceMappingsAsync(new ListEventSourceMappingsRequest { FunctionName = _eventDispatcherFunctionName, EventSourceArn = queueArn });

            if (mappings is not null)
            {
                foreach (var mapping in mappings.EventSourceMappings)
                {
                    if (!string.IsNullOrWhiteSpace(mapping?.UUID))
                    {
                        await _lambdaClient.DeleteEventSourceMappingAsync(new DeleteEventSourceMappingRequest { UUID = mapping.UUID });
                    }
                }
            }
        }

        #endregion

        public string GetTopicName(string eventName)
        {
            return eventName.Replace("IntegrationEvent", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        }

        private async Task<string?> GetTopicArnIfExistsAsync(string topicName, CancellationToken cancellationToken = default)
        {
            topicName = $"{topicName}.fifo";
            if (_topicArn.TryGetValue(topicName, out var cachedArn))
            {
                _logger.LogDebug("Retrieved topic ARN from cache: {TopicName} -> {TopicArn}", topicName, cachedArn);
                return cachedArn;
            }

            string nextToken = string.Empty;
            do
            {
                var response = await _snsClient.ListTopicsAsync(new ListTopicsRequest { NextToken = nextToken }, cancellationToken);
                if (response is null)
                {
                    return null;
                }
                foreach (var topic in response.Topics)
                {
                    var topicArn = topic.TopicArn;
                    _topicArn[topicArn.Substring(topicArn.LastIndexOf(':') + 1)] = topicArn;

                    if (topicArn.EndsWith($":{topicName}", StringComparison.OrdinalIgnoreCase))
                    {
                        return topicArn;
                    }
                }
                nextToken = response.NextToken;
            } while (!string.IsNullOrEmpty(nextToken));

            return null;
        }

        private static string CreateQueuePolicy(string topicArn, string queueArn)
        {
            // Define the policy structure as a C# object
            var policy = new
            {
                Version = "2012-10-17",
                Statement = new[]
                {
                    new
                    {
                        Effect = "Allow",
                        Principal = new { Service = "sns.amazonaws.com" },
                        Action = "sqs:SendMessage",
                        Resource = queueArn,
                        Condition = new
                        {
                            ArnEquals = new Dictionary<string, string>
                            {
                                { "aws:SourceArn", topicArn }
                            }
                        }
                    }
                }
            };

            // Serialize the object to a JSON string
            return JsonSerializer.Serialize(policy, new JsonSerializerOptions
            {
                WriteIndented = true // Optional: makes JSON easier to read
            });
        }

        private async Task<string> GetQueueArnAsync(string queueUrl, CancellationToken cancellationToken = default)
        {
            return (await _sqsClient.GetQueueAttributesAsync(queueUrl, new List<string> { "QueueArn" }, cancellationToken))
                             .Attributes["QueueArn"];
        }

        private async Task AddDefaultTagsForQueueAsync(string queueUrl, CancellationToken cancellationToken = default)
        {
            var defaultTagsDictionary = _defaultTags.ToDictionary(t => t.Key, t => t.Value);

            await _sqsClient.TagQueueAsync(new TagQueueRequest
            {
                QueueUrl = queueUrl,
                Tags = defaultTagsDictionary
            }, cancellationToken);
        }

        private async Task AddDefaultTagsToSnsTopicAsync(string topicArn, CancellationToken cancellationToken = default)
        {
            try
            {
                var defaultTagsDictionary = _defaultTags.Select(t => new Tag
                {
                    Key = t.Key,
                    Value = t.Value
                }).ToList();

                await _snsClient.TagResourceAsync(new Amazon.SimpleNotificationService.Model.TagResourceRequest
                {
                    ResourceArn = topicArn,
                    Tags = defaultTagsDictionary
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Not able to add Tags to Sns Topic with ARN {TopicArn}.", topicArn);
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // dispose resources
                _topicArn.Clear();
            }
        }
    }
}
