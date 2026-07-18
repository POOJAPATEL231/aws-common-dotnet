using Amazon.DynamoDBv2.Model;
using Domain.Common.Entities;
using Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Persistence.Common.AWS
{
    public interface IBaseDynamoDbSet
    {
        Task AddAsync(object entity, CancellationToken cancellationToken);
        Task UpdateAsync(object entity, CancellationToken cancellationToken);
        Task RemoveAsync(object entity, CancellationToken cancellationToken);

        TransactWriteItem GetAddTransactionItem(object entity);

        TransactWriteItem GetUpdateTransactionItem(object entity);

        TransactWriteItem GetDeleteTransactionItem(object entity);

        Task ExecuteTransactionAsync(List<TransactWriteItem> transactionItems, CancellationToken cancellationToken = default);
    }
}
