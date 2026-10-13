using OrderFlow.OrderService.Domain;

namespace OrderFlow.OrderService.UnitTests.Domain;

public class OrderTests
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    [Fact]
    public void Valid_order_is_placed_as_pending()
    {
        var result = Order.Place("customer-1", [new OrderLine("SKU-001", 2)], Clock);

        result.IsValid.ShouldBeTrue();
        result.Order!.Status.ShouldBe(OrderStatus.Pending);
        result.Order.Id.ShouldNotBe(Guid.Empty);
        result.Order.Lines.ShouldHaveSingleItem().ShouldBe(new OrderLine("SKU-001", 2));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_customer_is_rejected(string? customerId)
    {
        var result = Order.Place(customerId, [new OrderLine("SKU-001", 1)], Clock);

        result.IsValid.ShouldBeFalse();
        result.Errors.Keys.ShouldBe(["customerId"]);
    }

    [Fact]
    public void Order_without_lines_is_rejected()
    {
        var result = Order.Place("customer-1", [], Clock);

        result.Errors.Keys.ShouldBe(["lines"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Quantity_below_one_is_rejected(int quantity)
    {
        var result = Order.Place("customer-1", [new OrderLine("SKU-001", 2), new OrderLine("SKU-002", quantity)], Clock);

        result.Errors.Keys.ShouldBe(["lines[1].quantity"]);
    }

    [Fact]
    public void Every_invalid_field_is_reported()
    {
        var result = Order.Place("", [new OrderLine("", 0)], Clock);

        result.Errors.Keys.ShouldBe(["customerId", "lines[0].sku", "lines[0].quantity"], ignoreOrder: true);
    }
}
