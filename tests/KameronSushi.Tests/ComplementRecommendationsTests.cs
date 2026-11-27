using System.Reflection;
using KameronSushi.Api.Controllers;
using KameronSushi.Application.Abstractions;
using KameronSushi.Application.Pos;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace KameronSushi.Tests;

public sealed class ComplementRecommendationsTests
{
    [Fact]
    public async Task SanitizesDuplicateIdsAndReturnsOnlyAggregateRecommendations()
    {
        var store = DispatchProxy.Create<IPosStore, ComplementStoreProxy>();
        var proxy = (ComplementStoreProxy)(object)store;
        var controller = new CustomerOrderingController(store, null!);
        var response = await controller.GetComplements([1, 1], [2, 2, 3], CancellationToken.None);
        var result = Assert.IsAssignableFrom<IReadOnlyList<ComplementRecommendation>>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal(new long[] { 1 }, proxy.ProductIds);
        Assert.Equal(new long[] { 2, 3 }, proxy.CandidateIds);
        Assert.Equal(new ComplementRecommendation(2, "together"), Assert.Single(result));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(61)]
    public async Task RejectsInvalidOrOversizedSelections(int value)
    {
        var store = DispatchProxy.Create<IPosStore, ComplementStoreProxy>();
        var controller = new CustomerOrderingController(store, null!);
        var candidates = value == 61 ? Enumerable.Range(1, 61).Select(id => (long)id).ToArray() : new long[] { value };
        var response = await controller.GetComplements([1], candidates, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(response.Result);
        Assert.Equal(0, ((ComplementStoreProxy)(object)store).Calls);
    }
}

public class ComplementStoreProxy : DispatchProxy
{
    public long[] ProductIds { get; private set; } = [];
    public long[] CandidateIds { get; private set; } = [];
    public int Calls { get; private set; }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name != nameof(IPosStore.GetComplementRecommendationsAsync)) throw new NotSupportedException();
        Calls++;
        ProductIds = ((IReadOnlyList<long>)args![0]!).ToArray();
        CandidateIds = ((IReadOnlyList<long>)args[1]!).ToArray();
        return Task.FromResult<IReadOnlyList<ComplementRecommendation>>([new(2, "together")]);
    }
}
