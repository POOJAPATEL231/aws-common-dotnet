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

        public static async Task<IApplicationBuilder> UsePersistenceDynamoAsync<TContext>(this IApplicationBuilder app, DynamoDbRepositoryOptions dynamoOptions)
             where TContext : BaseDynamoDbContext
        {
            using (var scope = app.ApplicationServices.CreateScope())
            {
                var provider = scope.ServiceProvider;
                var context = provider.GetRequiredService<TContext>();
                // Ensure DynamoDB tables are created
                await EnsureDynamoDbTablesCreatedAsync(context, provider, dynamoOptions);
            }
            return app;
        }

        // Method to ensure tables are created for all entities using IDynamoDbTableProvider
        private static async Task EnsureDynamoDbTablesCreatedAsync<TContext>(
            TContext context,
            IServiceProvider serviceProvider,
            DynamoDbRepositoryOptions options,
            CancellationToken cancellationToken = default)
            where TContext : BaseDynamoDbContext
        {
            // Reflectively get all properties of type IDynamoDbSet<TEntity> from the context
            var properties = context.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(IDynamoDbSet<>));

            foreach (var property in properties)
            {
                // Get the entity type for the current IDynamoDbSet<TEntity>
                var entityType = property.PropertyType.GenericTypeArguments[0];
                // Get all services registered as IDynamoDbTableProvider
                var allProviders = serviceProvider.GetServices(typeof(IDynamoDbTableProvider));

                // Use LINQ to filter out the specific DynamoDbTableProvider<TEntity> based on TEntity
                var provider = allProviders.FirstOrDefault(p =>
                {
                    var providerType = p!.GetType();

                    // Check if the provider is a generic type and if it's DynamoDbTableProvider<>
                    if (providerType.IsGenericType && providerType.GetGenericTypeDefinition() == typeof(DynamoDbTableProvider<>))
                    {
                        // Get the type argument (TEntity) for the generic type and check if it matches the requested TEntity
                        var type = providerType.GetGenericArguments()[0];
                        return type == entityType;
                    }

                    return false;
                });
                var dynamoDbProvider = provider as IDynamoDbTableProvider;

                if (dynamoDbProvider != null)
                {
                    // Check if the table exists and create it if it doesn't
                    bool tableExists = await dynamoDbProvider.TableExistsAsync(cancellationToken);
                    if (!tableExists)
                    {
                        var entityName = entityType.Name;

                        // Try to find the matching entity setting in DocEntitySettings
                        var entitySetting = options.DocEntitySettings?.FirstOrDefault(s => s.EntityName == entityName);

                        // Set read and write capacity units based on whether the entity setting is found
                        long readCapacityUnits = entitySetting?.ReadCapacityUnits ?? options.ReadCapacityUnits;
                        long writeCapacityUnits = entitySetting?.WriteCapacityUnits ?? options.WriteCapacityUnits;

                        // Create the table using the capacity units from either DocEntitySettings or global options
                        await dynamoDbProvider.CreateTableAsync(
                            readCapacityUnits: readCapacityUnits,
                            writeCapacityUnits: writeCapacityUnits,
                            cancellationToken: cancellationToken
                        );
                    }
                    await dynamoDbProvider.EnableTtlAsync(cancellationToken);
                }
            }
        }
    }
}
