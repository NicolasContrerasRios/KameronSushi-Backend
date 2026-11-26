using KameronSushi.Shared;
using Xunit;

namespace KameronSushi.Tests;

public class DeliveryZonesTests
{
    [Theory]
    [InlineData("Las Compañías", 2000)]
    [InlineData("Puertas del Mar", 3000)]
    [InlineData("Centro de La Serena", 2500)]
    [InlineData("La Antena", 3000)]
    [InlineData("La Florida", 3000)]
    [InlineData("Caleta San Pedro", 3000)]
    [InlineData("Villa Bicentenario", 2500)]
    public void UsesConfiguredDeliveryRate(string zone, int expected) =>
        Assert.Equal(expected, DeliveryZones.GetFee(zone));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Coquimbo")]
    public void RejectsMissingOrUnsupportedZone(string? zone) =>
        Assert.Throws<ArgumentException>(() => DeliveryZones.GetFee(zone));
}
