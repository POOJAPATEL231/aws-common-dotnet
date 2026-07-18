using AwsShowcase.Core.Abstractions;
using AwsShowcase.Core.Orders;
using AwsShowcase.Entity;
using Domain.Common.Event;
using Domain.Common.Repositories;
using Moq;
using Xunit;

namespace AwsShowcase.Tests;

public class CreateOrderCommandHandlerTests
{
    [Fact]
    public async Task Create_AddsOrder_StagesOutboxEvent_AndSaves()
    {
        var repository = new Mock<IOrderRepository>();
        var outbox = new Mock<IIntegrationEventOutbox>();
        var unitOfWork = new Mock<IUnitOfWork>();
        Order? added = null;
        repository.Setup(r => r.Add(It.IsAny<Order>())).Callback<Order>(o => { o.SetId("order:1"); added = o; });

        var handler = new CreateOrderCommandHandler(repository.Object, outbox.Object, unitOfWork.Object);
        var dto = await handler.Handle(new CreateOrderCommand("alice@example.com", "Laptop", 2, 999.99), CancellationToken.None);

        Assert.NotNull(added);
        Assert.Equal("alice@example.com", dto.CustomerEmail);
        Assert.Equal("order:1", dto.Id);
        Assert.Equal(nameof(OrderStatus.Pending), dto.Status);

        // The event must be staged BEFORE the save so it joins the same transaction.
        outbox.Verify(o => o.Enqueue(It.Is<IntegrationEvent>(e =>
            ((OrderCreatedIntegrationEvent)e).OrderId == "order:1" &&
            ((OrderCreatedIntegrationEvent)e).CustomerEmail == "alice@example.com")), Times.Once);
        unitOfWork.Verify(u => u.SaveEntitiesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class UpdateOrderStatusCommandHandlerTests
{
    [Fact]
    public async Task Update_ChangesStatus_AndSaves()
    {
        var order = new Order { CustomerEmail = "a@b.c", Status = OrderStatus.Pending };
        order.SetId("order:1");
        var repository = new Mock<IOrderRepository>();
        repository.Setup(r => r.GetAsync("order:1", It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new UpdateOrderStatusCommandHandler(repository.Object, unitOfWork.Object);
        var dto = await handler.Handle(new UpdateOrderStatusCommand("order:1", OrderStatus.Shipped), CancellationToken.None);

        Assert.Equal(nameof(OrderStatus.Shipped), dto!.Status);
        repository.Verify(r => r.Update(order), Times.Once);
        unitOfWork.Verify(u => u.SaveEntitiesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_UnknownOrder_ReturnsNull_WithoutSaving()
    {
        var repository = new Mock<IOrderRepository>();
        repository.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Order?)null);
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new UpdateOrderStatusCommandHandler(repository.Object, unitOfWork.Object);

        Assert.Null(await handler.Handle(new UpdateOrderStatusCommand("missing", OrderStatus.Paid), CancellationToken.None));
        unitOfWork.Verify(u => u.SaveEntitiesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_CancelledOrder_RejectsStatusChange()
    {
        var order = new Order { Status = OrderStatus.Cancelled };
        order.SetId("order:1");
        var repository = new Mock<IOrderRepository>();
        repository.Setup(r => r.GetAsync("order:1", It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var handler = new UpdateOrderStatusCommandHandler(repository.Object, Mock.Of<IUnitOfWork>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new UpdateOrderStatusCommand("order:1", OrderStatus.Shipped), CancellationToken.None));
    }
}

public class DeleteOrderCommandHandlerTests
{
    [Fact]
    public async Task Delete_RemovesAndSaves_WhenFound()
    {
        var order = new Order();
        order.SetId("order:1");
        var repository = new Mock<IOrderRepository>();
        repository.Setup(r => r.GetAsync("order:1", It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new DeleteOrderCommandHandler(repository.Object, unitOfWork.Object);

        Assert.True(await handler.Handle(new DeleteOrderCommand("order:1"), CancellationToken.None));
        repository.Verify(r => r.Remove(order), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_UnknownOrder_ReturnsFalse()
    {
        var repository = new Mock<IOrderRepository>();
        repository.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Order?)null);

        var handler = new DeleteOrderCommandHandler(repository.Object, Mock.Of<IUnitOfWork>());

        Assert.False(await handler.Handle(new DeleteOrderCommand("missing"), CancellationToken.None));
    }
}

public class GetOrderByIdQueryHandlerTests
{
    [Fact]
    public async Task Get_MapsEntityToDto()
    {
        var order = new Order { CustomerEmail = "a@b.c", ProductName = "Laptop", Quantity = 2, Price = 10.5, Status = OrderStatus.Paid, Tags = { "vip" } };
        order.SetId("order:1");
        var repository = new Mock<IOrderRepository>();
        repository.Setup(r => r.GetAsync("order:1", It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var dto = await new GetOrderByIdQueryHandler(repository.Object).Handle(new GetOrderByIdQuery("order:1"), CancellationToken.None);

        Assert.NotNull(dto);
        Assert.Equal("order:1", dto!.Id);
        Assert.Equal("Laptop", dto.ProductName);
        Assert.Equal("Paid", dto.Status);
        Assert.Contains("vip", dto.Tags);
    }

    [Fact]
    public async Task Get_UnknownOrder_ReturnsNull()
    {
        var repository = new Mock<IOrderRepository>();
        repository.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Order?)null);

        Assert.Null(await new GetOrderByIdQueryHandler(repository.Object).Handle(new GetOrderByIdQuery("missing"), CancellationToken.None));
    }
}
