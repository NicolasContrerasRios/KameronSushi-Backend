namespace KameronSushi.Shared;

public static class DeliveryZones
{
    public static IReadOnlyDictionary<string, decimal> Rates { get; } = new Dictionary<string, decimal>
    {
        ["Las Compañías"] = 2000,
        ["Puertas del Mar"] = 3000,
        ["Centro de La Serena"] = 2500,
        ["La Antena"] = 3000,
        ["La Florida"] = 3000,
        ["Caleta San Pedro"] = 3000,
        ["Villa Bicentenario"] = 2500
    };

    public static decimal GetFee(string? zone) => zone is not null && Rates.TryGetValue(zone, out var fee)
        ? fee
        : throw new ArgumentException("Selecciona una zona de delivery disponible.");
}
