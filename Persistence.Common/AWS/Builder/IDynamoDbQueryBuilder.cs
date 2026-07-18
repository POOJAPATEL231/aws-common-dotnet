using Amazon.DynamoDBv2.Model;
using Domain.Common.Entities;
using System.Linq.Expressions;

namespace Persistence.Common.AWS.Builder
{
    public interface IDynamoDbQueryBuilder<T> where T : DocEntity
    {
        IDynamoDbQueryBuilder<T> WithExpression(Expression<Func<T, bool>> expression);

        (KeyFilter? KeyFilterExpression, string FilterExpression,
            Dictionary<string, AttributeValue> ExpressionAttributeValues,
            Dictionary<string, string> Aliases) BuildFromExpression(Expression expression);

        (KeyFilter? KeyFilterExpression, string FilterExpression,
            Dictionary<string, AttributeValue> ExpressionAttributeValues,
            Dictionary<string, string> Aliases) Build();
    }
}
