using Amazon.DynamoDBv2.Model;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore.Query;
using Persistence.Common.AWS.Builder;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Persistence.Common.AWS
{
    public class DynamoDbQueryProvider<TEntity> : IAsyncQueryProvider where TEntity : DocEntity
    {
        private readonly IDynamoDbDocProvider<TEntity> _docProvider;

        private readonly IDynamoDbTransactionExecutor _transactionExecutor;

        public DynamoDbQueryProvider(IDynamoDbDocProvider<TEntity> docProvider, IDynamoDbTransactionExecutor transactionExecutor)
        {
            _docProvider = docProvider;
            _transactionExecutor = transactionExecutor;
        }

        public IQueryable CreateQuery(Expression expression)
        {
            return new DynamoDbSet<TEntity>(_docProvider, _transactionExecutor, expression);
        }

        IQueryable<TElement> IQueryProvider.CreateQuery<TElement>(Expression expression)
        {
            if (!typeof(DocEntity).IsAssignableFrom(typeof(TElement)))
            {
                throw new InvalidOperationException($"TElement must be a type that derives from {nameof(DocEntity)}.");
            }

            // Safe to cast because of the runtime check
            return (IQueryable<TElement>)CreateQueryInternal(typeof(TElement), expression);
        }

        IQueryable IQueryProvider.CreateQuery(Expression expression)
        {
            return new DynamoDbSet<TEntity>(_docProvider, _transactionExecutor, expression);
        }

        private IQueryable CreateQueryInternal(Type elementType, Expression expression)
        {
            var dynamoDbSetType = typeof(DynamoDbSet<>).MakeGenericType(elementType);

            return (IQueryable)Activator.CreateInstance(dynamoDbSetType, _docProvider, _transactionExecutor, expression)!;
        }

        public async Task<TResult> ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            var dynamoDbQueryBuilder = new DynamoDbQueryBuilder<TEntity>();
            var (keyConditionExpression, filterExpression, expressionAttributeValues, expressionAttributeNames) = dynamoDbQueryBuilder.BuildFromExpression(expression);

            if (typeof(TResult) == typeof(IAsyncEnumerable<TEntity>))
            {
                // Return an IAsyncEnumerable<TEntity> for async iteration
                return (TResult)(object)GetAsyncEnumerableAsync(filterExpression, keyConditionExpression, expressionAttributeValues, expressionAttributeNames, cancellationToken);
            }

            var results = keyConditionExpression is not null && keyConditionExpression.CanUseKeyFilter()
                                    ? await _docProvider.GetItemsByQueryAsync(
                                        filterExpression,
                                        keyConditionExpression.GenerateFinalKeyFilter(),
                                        expressionAttributeValues,
                                        expressionAttributeNames,
                                        cancellationToken)
                                    : await _docProvider.GetItemsByQueryAsync(
                                        filterExpression,
                                        expressionAttributeValues,
                                        expressionAttributeNames,
                                        cancellationToken);

            if (typeof(TResult).IsGenericType &&
                (typeof(TResult).GetGenericTypeDefinition() == typeof(IEnumerable<>) ||
                 typeof(TResult).GetGenericTypeDefinition() == typeof(List<>)))
            {
                return (TResult)(object)(results?.ToList() ?? new List<TEntity>());
            }

            // Scalar/aggregate results (Count(), Any(), ...) must not be cast to TEntity.
            if (typeof(TResult) == typeof(int))
            {
                return (TResult)(object)(results?.Count() ?? 0);
            }

            if (typeof(TResult) == typeof(long))
            {
                return (TResult)(object)(results?.LongCount() ?? 0L);
            }

            if (typeof(TResult) == typeof(bool))
            {
                return (TResult)(object)(results?.Any() ?? false);
            }

            var result = results?.FirstOrDefault() ?? default;
            return (TResult)(object)result!;
        }

        private async IAsyncEnumerable<TEntity> GetAsyncEnumerableAsync(string filterExpression,
             KeyFilter? keyConditionExpression,
            Dictionary<string, AttributeValue> expressionAttributeValues,
            Dictionary<string, string> expressionAttributeNames,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var items = keyConditionExpression is not null && keyConditionExpression.CanUseKeyFilter()
                        ? await _docProvider.GetItemsByQueryAsync(
                            filterExpression,
                            keyConditionExpression.GenerateFinalKeyFilter(),
                            expressionAttributeValues,
                            expressionAttributeNames,
                            cancellationToken)
                        : await _docProvider.GetItemsByQueryAsync(
                            filterExpression,
                            expressionAttributeValues,
                            expressionAttributeNames,
                            cancellationToken);


            foreach (var item in items ?? Enumerable.Empty<TEntity>())
            {
                yield return item;
            }
        }

        [SuppressMessage("Maintainability", "S4462", Justification = "IAsyncQueryProvider requires these methods to be synchronous. Async methods are already implemented.")]
        public TResult Execute<TResult>(Expression expression)
        {
            return ExecuteAsync<TResult>(expression).Result;
        }

        [SuppressMessage("Maintainability", "S4462", Justification = "IAsyncQueryProvider requires these methods to be synchronous. Async methods are already implemented.")]
        public object? Execute(Expression expression)
        {
            return ExecuteAsync<TEntity>(expression).Result;
        }

        [SuppressMessage("Maintainability", "S4462", Justification = "IAsyncQueryProvider requires these methods to be synchronous. Async methods are already implemented.")]
        TResult IAsyncQueryProvider.ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken)
        {
            return ExecuteAsync<TResult>(expression, cancellationToken).Result;
        }
    }
}
