using System.Reflection;
using KameronSushi.Api.Controllers;
using KameronSushi.Application.Abstractions;
using KameronSushi.Application.Admin;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace KameronSushi.Tests;

public sealed class ProductAvailabilityTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdatesOnlyRequestedAvailability(bool available)
    {
        var store = DispatchProxy.Create<IAdminStore, AvailabilityStoreProxy>();
        var proxy = (AvailabilityStoreProxy)(object)store;
        var controller = new AdminController(store, null!, null!);
        var response = await controller.SetProductAvailability(12, new(available), CancellationToken.None);
        var product = Assert.IsType<AdminProduct>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal(available, product.Available);
        Assert.Equal(12, proxy.ProductId);
        Assert.Equal("Hand roll", product.Name);
        Assert.Equal(3500m, product.Price);
        Assert.True(product.Active);
        Assert.Equal(1, proxy.Calls);
    }

    [Fact]
    public async Task MissingAvailabilityDoesNotModifyProduct()
    {
        var store = DispatchProxy.Create<IAdminStore, AvailabilityStoreProxy>();
        var controller = new AdminController(store, null!, null!);
        var response = await controller.SetProductAvailability(12, new(null), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(response.Result);
        Assert.Equal(0, ((AvailabilityStoreProxy)(object)store).Calls);
    }

    [Fact]
    public async Task MissingProductReturnsNotFound()
    {
        var store = DispatchProxy.Create<IAdminStore, AvailabilityStoreProxy>();
        ((AvailabilityStoreProxy)(object)store).Exists = false;
        var controller = new AdminController(store, null!, null!);
        var response = await controller.SetProductAvailability(12, new(false), CancellationToken.None);
        Assert.IsType<NotFoundResult>(response.Result);
    }
}

public class AvailabilityStoreProxy : DispatchProxy
{
    public bool Exists { get; set; } = true;
    public int Calls { get; private set; }
    public long ProductId { get; private set; }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name != nameof(IAdminStore.SetProductAvailabilityAsync))
            throw new NotSupportedException();
        Calls++;
        ProductId = (long)args![0]!;
        return Task.FromResult<AdminProduct?>(Exists
            ? new AdminProduct(ProductId, 1, "Hand rolls", "Hand roll", null, 3500m, null,
                true, (bool)args[1]!, false, 0)
            : null);
    }
}
