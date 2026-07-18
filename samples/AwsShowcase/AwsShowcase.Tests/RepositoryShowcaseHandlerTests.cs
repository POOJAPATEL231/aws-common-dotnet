using AwsShowcase.Core.Abstractions;
using AwsShowcase.Core.Orders;
using AwsShowcase.Entity;
using Domain.Common.Repositories;
using Moq;
using System.Linq.Expressions;
using Xunit;

namespace AwsShowcase.Tests;

public class CreateOrdersBatchCommandHandlerTests
{
    [Fact]
    public async Task Batch_AddsAll_EnqueuesEventPerOrder_AndSavesOnce()
    {
        var repository = new Mock<IOrderRepository>();
        var outbox = new Mock<IIntegrationEventOutbox>();
        var unitOfWork = new Mock<IUnitOfWork>();
        repository.Setup(r => r.AddRange(It.IsAny<IEnumerable<Order>>()))
            .Callback<IEnumerable<Order>>(orders =>
            {
                var i = 0;
                foreach (var order in orders) { order.SetId($"order:{++i}"); }
            });

        var handler = new CreateOrdersBatchCommandHandler(repository.Object, outbox.Object, unitOfWork.Object);
        var result = await handler.Handle(new CreateOrdersBatchCommand(new List<OrderLineDto>
        {
            new("a@b.c", "Laptop", 1, 999),
            new("a@b.c", "Mouse", 2, 25)
        }), CancellationToken.None);

        Assert.Equal(2, result.Count);
        outbox.Verify(o => o.Enqueue(It.IsAny<Domain.Common.Event.IntegrationEvent>()), Times.Exactly(2));
        unitOfWork.Verify(u => u.SaveEntitiesAsync(It.IsAny<CancellationToken>()), Times.Once); // ONE atomic transaction
    }
}

public class DeleteOrdersByCustomerCommandHandlerTests
{
    [Fact]
    public async Task Purge_RemovesRange_AndReturnsCount()
    {
        var orders = new List<Order> { new(), new(), new() };
        var repository = new Mock<IOrderRepository>();
        repository.Setup(r => r.GetAsync(It.IsAny<Expression<Func<Order, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders);
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new DeleteOrdersByCustomerCommandHandler(repository.Object, unitOfWork.Object);
        var deleted = await handler.Handle(new DeleteOrdersByCustomerCommand("a@b.c"), CancellationToken.None);

        Assert.Equal(3, deleted);
        repository.Verify(r => r.RemoveRange(orders), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Purge_NoOrders_DoesNotSave()
    {
        var repository = new Mock<IOrderRepository>();
        repository.Setup(r => r.GetAsync(It.IsAny<Expression<Func<Order, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Order>());
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new DeleteOrdersByCustomerCommandHandler(repository.Object, unitOfWork.Object);

        Assert.Equal(0, await handler.Handle(new DeleteOrdersByCustomerCommand("nobody"), CancellationToken.None));
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}

public class ApplyDiscountCommandHandlerTests
{
    [Fact]
    public async Task Discount_UpdatesEachOrder_AndReportsModifiedCount()
    {
        var orders = new List<Order>
        {
            new() { Price = 100, Status = OrderStatus.Pending },
            new() { Price = 50, Status = OrderStatus.Pending }
        };
        var repository = new Mock<IOrderRepository>();
        repository.Setup(r => r.GetAsync(It.IsAny<Expression<Func<Order, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders);
        repository.Setup(r => r.GetModified()).Returns(orders);
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new ApplyDiscountCommandHandler(repository.Object, unitOfWork.Object);
        var modified = await handler.Handle(new ApplyDiscountCommand("a@b.c", 10), CancellationToken.None);

        Assert.Equal(2, modified);
        Assert.Equal(90, orders[0].Price);
        Assert.Equal(45, orders[1].Price);
        repository.Verify(r => r.Update(It.IsAny<Order>()), Times.Exactly(2));
        unitOfWork.Verify(u => u.SaveEntitiesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class CustomerHasOrdersQueryHandlerTests
{
    [Fact]
    public async Task Exists_DelegatesToAnyAsync()
    {
        var repository = new Mock<IOrderRepository>();
        repository.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Order, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new CustomerHasOrdersQueryHandler(repository.Object);

        Assert.True(await handler.Handle(new CustomerHasOrdersQuery("a@b.c"), CancellationToken.None));
    }
}
