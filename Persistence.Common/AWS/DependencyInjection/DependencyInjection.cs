using Amazon.DynamoDBv2;
using Application.Common.AWS;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Persistence.Common.AWS.Builder;
using Persistence.Common.AWS.Repositories;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Persistence.Common.AWS.DependencyInjection
{
    public static class DependencyInjection
    {
        [SuppressMessage("Maintainability", "S4462", Justification = "DI methods are synchronous, requiring .GetAwaiter().GetResult() to handle async tasks.")]
        public static IServiceCollection AddPersistenceDynamoDb<TContext>(this IServiceCollection services,
            IConfiguration configuration, IHostEnvironment env)
            where TContext : BaseDynamoDbContext
        {
            // Initialize in-memory configuration cache
            InMemoryDynamoDBEntitiesConfiguration.InitializeConfigurationCache(typeof(TContext).Assembly);

            // Register the necessary services
            services.AddAwsServiceWithConfiguration<IAmazonDynamoDB>(configuration);

            var serviceRegistrations = new (Type serviceInterface, Type serviceImplementation, ServiceLifetime lifetime)[]
            {
                (typeof(IDynamoDbDocProvider<>), typeof(DynamoDbDocProvider<>), ServiceLifetime.Scoped),
                (typeof(IDynamoDbSet<>), typeof(DynamoDbSet<>), ServiceLifetime.Scoped)
            };

            var properties = typeof(TContext)
               .GetProperties(BindingFlags.Public | BindingFlags.Instance)
               .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(IDynamoDbSet<>));

            foreach (var property in properties)
            {
                // Get the entity type for the current IDynamoDbSet<TEntity>
                var entityType = property.PropertyType.GenericTypeArguments[0];
                foreach (var (serviceInterface, serviceImplementation, lifetime) in serviceRegistrations)
                {
                    // Check if the service interface is open generic (has <T> or similar)
                    if (serviceInterface.IsGenericTypeDefinition)
                    {
                        // Make the generic type for the current entity type
                        var genericServiceInterface = serviceInterface.MakeGenericType(entityType);
                        var genericServiceImplementation = serviceImplementation.MakeGenericType(entityType);

                        // Register based on lifetime
                        switch (lifetime)
                        {
                            case ServiceLifetime.Singleton:
                                services.AddSingleton(genericServiceInterface, genericServiceImplementation);
                                break;
                            case ServiceLifetime.Scoped:
                                services.AddScoped(genericServiceInterface, genericServiceImplementation);
                                break;
                            case ServiceLifetime.Transient:
                                services.AddTransient(genericServiceInterface, genericServiceImplementation);
                                break;
                            default:
                                services.AddTransient(genericServiceInterface, genericServiceImplementation);
                                break;
                        }
                    }
                }

                var tableProviderImplementation = typeof(DynamoDbTableProvider<>).MakeGenericType(entityType);

                // Register the non-generic interface with a transient lifetime
                services.AddSingleton(typeof(IDynamoDbTableProvider), tableProviderImplementation);
            }

            services.AddSingleton(typeof(IDynamoDbTransactionExecutor), typeof(DynamoDbTransactionExecutor));
            services.AddScoped<TContext>();

            return services;
        }

        /// <summary>
        /// Registers the transactional outbox: the OutboxMessage set/providers, its table
        /// provider, and the background dispatcher that publishes pending messages to the
        /// registered <see cref="Application.Common.Event.IIntegrationEventPublisher"/>.
        /// </summary>
        public static IServiceCollection AddDynamoDbOutbox(this IServiceCollection services, Outbox.OutboxOptions? options = null)
        {
            services.AddScoped<IDynamoDbDocProvider<Outbox.OutboxMessage>, DynamoDbDocProvider<Outbox.OutboxMessage>>();
            services.AddScoped<IDynamoDbSet<Outbox.OutboxMessage>, DynamoDbSet<Outbox.OutboxMessage>>();
            services.AddSingleton(typeof(IDynamoDbTableProvider), typeof(DynamoDbTableProvider<Outbox.OutboxMessage>));
            services.AddSingleton(options ?? new Outbox.OutboxOptions());
            services.AddHostedService<Outbox.OutboxDispatcherService>();
            return services;
        }

        /// <summary>Registers the DynamoDB-backed distributed lock.</summary>
        public static IServiceCollection AddDynamoDbDistributedLock(this IServiceCollection services, Locking.DynamoDbLockOptions? options = null)
        {
            services.AddSingleton(options ?? new Locking.DynamoDbLockOptions());
            services.AddSingleton<Application.Common.Locking.IDistributedLock, Locking.DynamoDbDistributedLock>();
            return services;
        }

        public static async Task<IApplicationBuilder> UsePersistenceDynamoAsync<TContext>(this IApplicationBuilder app, DynamoDbRepositoryOptions dynamoOptions)
             where TContext : BaseDynamoDbContext
        {
            using (var scope = app.ApplicationServices.CreateScope())
            {
                var provider = scope.ServiceProvider;
                // Resolve the context so misconfigured wiring fails at startup, not first request.
                provider.GetRequiredService<TContext>();
                // Ensure DynamoDB tables are created
                await EnsureDynamoDbTablesCreatedAsync(provider, dynamoOptions);
            }
            return app;
        }

        /// <summary>
        /// Creates every missing table for EVERY registered IDynamoDbTableProvider - not
        /// just entities exposed as context properties. This matters for infrastructure
        /// entities like the transactional OutboxMessage, whose provider is registered by
        /// AddDynamoDbOutbox but which no context property references: reflecting over
        /// context properties would silently skip its table and the first transactional
        /// save that stages an outbox message would fail.
        /// </summary>
        internal static async Task EnsureDynamoDbTablesCreatedAsync(
            IServiceProvider serviceProvider,
            DynamoDbRepositoryOptions options,
            CancellationToken cancellationToken = default)
        {
            var providers = serviceProvider.GetServices(typeof(IDynamoDbTableProvider)).OfType<IDynamoDbTableProvider>();

            foreach (var tableProvider in providers)
            {
                // Capacity overrides are matched by the provider's entity type name.
                var entityName = tableProvider.GetType().GenericTypeArguments.FirstOrDefault()?.Name;
                var entitySetting = options.DocEntitySettings?.FirstOrDefault(s => s.EntityName == entityName);

                if (!await tableProvider.TableExistsAsync(cancellationToken))
                {
                    await tableProvider.CreateTableAsync(
                        readCapacityUnits: entitySetting?.ReadCapacityUnits ?? options.ReadCapacityUnits,
                        writeCapacityUnits: entitySetting?.WriteCapacityUnits ?? options.WriteCapacityUnits,
                        cancellationToken: cancellationToken);
                }

                await tableProvider.EnableTtlAsync(cancellationToken);
            }
        }
    }
}
