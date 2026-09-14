using System.Globalization;
using System.Collections.Concurrent;
using KameronSushi.Application.Abstractions;
using KameronSushi.Application.WhatsApp;

namespace KameronSushi.Application.Features.WhatsApp;

public sealed class WhatsAppConversationService(
    IWhatsAppStore store,
    IWhatsAppMessageSender sender)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ConversationLocks = new();

    public async Task ProcessAsync(IncomingWhatsAppMessage incoming, CancellationToken cancellationToken)
    {
        var conversationLock = ConversationLocks.GetOrAdd(incoming.WaId, _ => new SemaphoreSlim(1, 1));
        await conversationLock.WaitAsync(cancellationToken);
        try
        {
            await ProcessCoreAsync(incoming, cancellationToken);
            await store.MarkInboundProcessedAsync(incoming.ProviderMessageId, cancellationToken);
        }
        finally
        {
            conversationLock.Release();
        }
    }

    private async Task ProcessCoreAsync(IncomingWhatsAppMessage incoming, CancellationToken cancellationToken)
    {
        var registration = await store.RegisterInboundAsync(incoming, cancellationToken);
        if (registration.IsDuplicate)
        {
            return;
        }

        var input = (incoming.SelectionId ?? incoming.Text ?? string.Empty).Trim();
        var normalized = input.ToLowerInvariant();

        if (normalized is "menu" or "menú" or "inicio")
        {
            if (registration.Context.ContainsKey("deliveryType"))
            {
                await ShowCategoriesAsync(registration.ConversationId, incoming.WaId, 0, cancellationToken);
            }
            else
            {
                await AskDeliveryTypeAsync(registration.ConversationId, incoming.WaId, cancellationToken);
            }
            return;
        }

        if (normalized == "volver")
        {
            if (registration.Context.ContainsKey("deliveryType"))
            {
                await ShowCategoriesAsync(registration.ConversationId, incoming.WaId, 0, cancellationToken);
            }
            else
            {
                await AskDeliveryTypeAsync(registration.ConversationId, incoming.WaId, cancellationToken);
            }
            return;
        }

        if (normalized is "pedido" or "carrito" or "cart:show")
        {
            await ShowCartAsync(registration.ConversationId, incoming.WaId, cancellationToken);
            return;
        }

        if (normalized is "finalizar" or "cart:finish")
        {
            await ShowPaymentMethodsAsync(registration.ConversationId, incoming.WaId, cancellationToken);
            return;
        }

        switch (registration.CurrentStep)
        {
            case "inicio":
                await AskDeliveryTypeAsync(registration.ConversationId, incoming.WaId, cancellationToken);
                break;
            case "seleccionando_entrega":
                await HandleDeliveryTypeAsync(registration.ConversationId, incoming, normalized, cancellationToken);
                break;
            case "ingresando_direccion":
                await HandleAddressAsync(registration.ConversationId, incoming.WaId, input, cancellationToken);
                break;
            case "seleccionando_categoria":
                await HandleCategoryAsync(registration.ConversationId, incoming.WaId, normalized, cancellationToken);
                break;
            case "seleccionando_producto":
                await HandleProductAsync(registration.ConversationId, incoming.WaId, normalized, cancellationToken);
                break;
            case "seleccionando_opcion_roll":
                await HandleRollOptionAsync(registration.ConversationId, incoming.WaId, normalized, cancellationToken);
                break;
            case "seleccionando_envoltura":
                await HandleWrappingAsync(registration.ConversationId, incoming.WaId, normalized, cancellationToken);
                break;
            case "seleccionando_salsa":
                await HandleSauceAsync(registration.ConversationId, incoming.WaId, normalized, cancellationToken);
                break;
            case "seleccionando_cantidad":
                await HandleQuantityAsync(registration, incoming, normalized, cancellationToken);
                break;
            case "revisando_carrito":
                await HandleCartActionAsync(registration.ConversationId, incoming.WaId, normalized, cancellationToken);
                break;
            case "seleccionando_pago":
                await HandlePaymentAsync(registration.ConversationId, incoming.WaId, normalized, cancellationToken);
                break;
            case "finalizado":
                await AskDeliveryTypeAsync(registration.ConversationId, incoming.WaId, cancellationToken);
                break;
            default:
                await SendAsync(registration.ConversationId, incoming.WaId,
                    new TextWhatsAppMessage("No pude reconocer esa opción. Escribe *menú* para comenzar nuevamente."), cancellationToken);
                break;
        }
    }

    public Task UpdateStatusAsync(string providerMessageId, string status, DateTimeOffset occurredAt, CancellationToken cancellationToken) =>
        store.UpdateMessageStatusAsync(providerMessageId, status, occurredAt, cancellationToken);

    private async Task AskDeliveryTypeAsync(long conversationId, string waId, CancellationToken cancellationToken)
    {
        await store.UpdateConversationAsync(conversationId, "seleccionando_entrega", new Dictionary<string, string?>(), cancellationToken);
        await SendAsync(conversationId, waId, new ButtonsWhatsAppMessage(
            "¡Bienvenido a Kameron Sushi! ¿Tu pedido es para retiro o delivery?",
            [new("delivery:pickup", "Retiro"), new("delivery:delivery", "Delivery")]), cancellationToken);
    }

    private async Task HandleDeliveryTypeAsync(long conversationId, IncomingWhatsAppMessage incoming, string input, CancellationToken cancellationToken)
    {
        if (input is "delivery:delivery" or "delivery")
        {
            await store.UpdateConversationAsync(conversationId, "ingresando_direccion",
                new Dictionary<string, string?> { ["deliveryType"] = "delivery" }, cancellationToken);
            await SendAsync(conversationId, incoming.WaId,
                new TextWhatsAppMessage("Escribe tu dirección como *calle, número, comuna*. Ejemplo: Los Alerces, 123, Temuco."), cancellationToken);
            return;
        }

        if (input is "delivery:pickup" or "retiro")
        {
            await store.UpdateConversationAsync(conversationId, "seleccionando_categoria",
                new Dictionary<string, string?>
                {
                    ["deliveryType"] = "retiro",
                    ["street"] = null,
                    ["streetNumber"] = null,
                    ["district"] = null
                }, cancellationToken);
            await ShowCategoriesAsync(conversationId, incoming.WaId, 0, cancellationToken);
            return;
        }

        await AskDeliveryTypeAsync(conversationId, incoming.WaId, cancellationToken);
    }

    private async Task HandleAddressAsync(long conversationId, string waId, string input, CancellationToken cancellationToken)
    {
        if (input.Equals("address:confirm", StringComparison.OrdinalIgnoreCase))
        {
            await ShowCategoriesAsync(conversationId, waId, 0, cancellationToken);
            return;
        }

        if (input.Equals("address:edit", StringComparison.OrdinalIgnoreCase))
        {
            await SendAsync(conversationId, waId,
                new TextWhatsAppMessage("Escribe nuevamente tu dirección como *calle, número, comuna*."), cancellationToken);
            return;
        }

        var parts = input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            await SendAsync(conversationId, waId,
                new TextWhatsAppMessage("Necesito calle, número y comuna separados por comas. Ejemplo: Los Alerces, 123, Temuco."), cancellationToken);
            return;
        }

        var street = parts[0];
        var number = parts[1];
        var district = string.Join(", ", parts.Skip(2));
        await store.UpdateConversationAsync(conversationId, "ingresando_direccion",
            new Dictionary<string, string?>
            {
                ["street"] = street,
                ["streetNumber"] = number,
                ["district"] = district
            }, cancellationToken);
        await SendAsync(conversationId, waId, new ButtonsWhatsAppMessage(
            $"Confirma tu dirección:\n{street} {number}, {district}",
            [new("address:confirm", "Confirmar"), new("address:edit", "Corregir")]), cancellationToken);
    }

    private async Task ShowCategoriesAsync(long conversationId, string waId, int page, CancellationToken cancellationToken)
    {
        var categories = await store.GetCategoriesAsync(cancellationToken);
        var rows = PageRows(categories, page,
            category => new WhatsAppListRow($"cat:{category.Id}", Shorten(category.Name, 24),
                category.Description is null ? null : Shorten(category.Description, 72)),
            nextPage => new WhatsAppListRow($"catpage:{nextPage}", "Ver más categorías"));

        await store.UpdateConversationAsync(conversationId, "seleccionando_categoria", new Dictionary<string, string?>(), cancellationToken);
        await SendAsync(conversationId, waId,
            new ListWhatsAppMessage("Elige una categoría. Tu carrito se conserva mientras navegas.", "Ver carta", "Categorías", rows), cancellationToken);
    }

    private async Task HandleCategoryAsync(long conversationId, string waId, string input, CancellationToken cancellationToken)
    {
        if (TryParseId(input, "catpage:", out var page))
        {
            await ShowCategoriesAsync(conversationId, waId, checked((int)page), cancellationToken);
            return;
        }

        if (!TryParseId(input, "cat:", out var categoryId))
        {
            await SendAsync(conversationId, waId, new TextWhatsAppMessage("Selecciona una categoría de la lista o escribe *menú*."), cancellationToken);
            return;
        }

        await ShowProductsAsync(conversationId, waId, categoryId, 0, cancellationToken);
    }

    private async Task ShowProductsAsync(long conversationId, string waId, long categoryId, int page, CancellationToken cancellationToken)
    {
        var products = await store.GetProductsAsync(categoryId, cancellationToken);
        var rows = PageRows(products, page,
            product => new WhatsAppListRow($"prod:{product.Id}", Shorten(product.Name, 24), $"${product.Price:N0}"),
            nextPage => new WhatsAppListRow($"prodpage:{categoryId}:{nextPage}", "Ver más productos"));

        if (rows.Count == 0)
        {
            await SendAsync(conversationId, waId, new TextWhatsAppMessage("Esta categoría no tiene productos disponibles. Escribe *menú* para volver."), cancellationToken);
            return;
        }

        await store.UpdateConversationAsync(conversationId, "seleccionando_producto", new Dictionary<string, string?>(), cancellationToken);
        await SendAsync(conversationId, waId,
            new ListWhatsAppMessage("Selecciona un producto. Escribe *menú* para volver a las categorías.", "Ver productos", "Productos", rows), cancellationToken);
    }

    private async Task HandleProductAsync(long conversationId, string waId, string input, CancellationToken cancellationToken)
    {
        if (TryParseProductPage(input, out var categoryId, out var page))
        {
            await ShowProductsAsync(conversationId, waId, categoryId, page, cancellationToken);
            return;
        }

        if (!TryParseId(input, "prod:", out var productId))
        {
            await SendAsync(conversationId, waId, new TextWhatsAppMessage("Selecciona un producto de la lista o escribe *menú*."), cancellationToken);
            return;
        }

        var product = await store.GetProductAsync(productId, cancellationToken);
        if (product is null)
        {
            await SendAsync(conversationId, waId, new TextWhatsAppMessage("Ese producto ya no está disponible. Escribe *menú* para actualizar la carta."), cancellationToken);
            return;
        }

        var context = new Dictionary<string, string?>
        {
            ["productId"] = product.Id.ToString(CultureInfo.InvariantCulture),
            ["productName"] = product.Name,
            ["unitPrice"] = product.Price.ToString(CultureInfo.InvariantCulture),
            ["rollOptionId"] = null,
            ["wrappingId"] = null,
            ["wrappingName"] = null,
            ["wrappingExtra"] = null,
            ["sauceId"] = null,
            ["sauceName"] = null
        };

        if (!product.RequiresConfiguration)
        {
            await store.UpdateConversationAsync(conversationId, "seleccionando_cantidad", context, cancellationToken);
            await AskQuantityAsync(conversationId, waId, product.Name, product.Price, cancellationToken);
            return;
        }

        await store.UpdateConversationAsync(conversationId, "seleccionando_opcion_roll", context, cancellationToken);
        await ShowRollOptionsAsync(conversationId, waId, product.Id, 0, cancellationToken);
    }

    private async Task ShowRollOptionsAsync(long conversationId, string waId, long productId, int page, CancellationToken cancellationToken)
    {
        var options = await store.GetRollOptionsAsync(productId, cancellationToken);
        var rows = PageRows(options, page,
            option => new WhatsAppListRow($"roll:{option.Id}", $"N.º {option.Number}", Shorten(option.Ingredients, 72)),
            nextPage => new WhatsAppListRow($"rollpage:{productId}:{nextPage}", "Ver más opciones"));
        await SendAsync(conversationId, waId,
            new ListWhatsAppMessage("Elige la combinación de ingredientes.", "Ver opciones", "Opciones", rows), cancellationToken);
    }

    private async Task HandleRollOptionAsync(long conversationId, string waId, string input, CancellationToken cancellationToken)
    {
        if (TryParseProductPage(input.Replace("rollpage:", "prodpage:"), out var pagedProductId, out var page))
        {
            await ShowRollOptionsAsync(conversationId, waId, pagedProductId, page, cancellationToken);
            return;
        }

        if (!TryParseId(input, "roll:", out var optionId))
        {
            await SendAsync(conversationId, waId, new TextWhatsAppMessage("Selecciona una opción de ingredientes."), cancellationToken);
            return;
        }

        var context = await store.GetConversationContextAsync(conversationId, cancellationToken);
        var option = await store.GetRollOptionAsync(optionId, cancellationToken);
        if (option is null || !context.TryGetValue("productId", out var productIdValue) ||
            !long.TryParse(productIdValue, out var productId) || option.ProductId != productId)
        {
            await SendAsync(conversationId, waId, new TextWhatsAppMessage("La selección expiró. Escribe *menú* para intentarlo nuevamente."), cancellationToken);
            return;
        }

        if (option.FixedWrappingId is not null)
        {
            await store.UpdateConversationAsync(conversationId, "seleccionando_salsa",
                new Dictionary<string, string?>
                {
                    ["rollOptionId"] = option.Id.ToString(CultureInfo.InvariantCulture),
                    ["wrappingId"] = option.FixedWrappingId.Value.ToString(CultureInfo.InvariantCulture)
                }, cancellationToken);
            await ShowSaucesAsync(conversationId, waId, cancellationToken);
            return;
        }

        await store.UpdateConversationAsync(conversationId, "seleccionando_envoltura",
            new Dictionary<string, string?> { ["rollOptionId"] = option.Id.ToString(CultureInfo.InvariantCulture) }, cancellationToken);
        await ShowWrappingsAsync(conversationId, waId, productId, 0, cancellationToken);
    }

    private async Task ShowWrappingsAsync(long conversationId, string waId, long productId, int page, CancellationToken cancellationToken)
    {
        var wrappings = await store.GetWrappingsAsync(productId, cancellationToken);
        var rows = PageRows(wrappings, page,
            wrapping => new WhatsAppListRow($"wrap:{wrapping.Id}", Shorten(wrapping.Name, 24),
                wrapping.ExtraPrice == 0 ? "Sin recargo" : $"+${wrapping.ExtraPrice:N0}"),
            nextPage => new WhatsAppListRow($"wrappage:{productId}:{nextPage}", "Ver más envolturas"));
        await SendAsync(conversationId, waId,
            new ListWhatsAppMessage("Elige la envoltura.", "Ver envolturas", "Envolturas", rows), cancellationToken);
    }

    private async Task HandleWrappingAsync(long conversationId, string waId, string input, CancellationToken cancellationToken)
    {
        if (TryParseProductPage(input.Replace("wrappage:", "prodpage:"), out var productId, out var page))
        {
            await ShowWrappingsAsync(conversationId, waId, productId, page, cancellationToken);
            return;
        }

        if (!TryParseId(input, "wrap:", out var wrappingId))
        {
            await SendAsync(conversationId, waId, new TextWhatsAppMessage("Selecciona una envoltura de la lista."), cancellationToken);
            return;
        }

        await store.UpdateConversationAsync(conversationId, "seleccionando_salsa",
            new Dictionary<string, string?> { ["wrappingId"] = wrappingId.ToString(CultureInfo.InvariantCulture) }, cancellationToken);
        await ShowSaucesAsync(conversationId, waId, cancellationToken);
    }

    private async Task ShowSaucesAsync(long conversationId, string waId, CancellationToken cancellationToken)
    {
        var sauces = await store.GetSaucesAsync(cancellationToken);
        await SendAsync(conversationId, waId, new ListWhatsAppMessage(
            "Elige la salsa incluida.", "Ver salsas", "Salsas",
            sauces.Take(10).Select(s => new WhatsAppListRow($"sauce:{s.Id}", Shorten(s.Name, 24))).ToArray()), cancellationToken);
    }

    private async Task HandleSauceAsync(long conversationId, string waId, string input, CancellationToken cancellationToken)
    {
        if (!TryParseId(input, "sauce:", out var sauceId))
        {
            await SendAsync(conversationId, waId, new TextWhatsAppMessage("Selecciona una salsa de la lista."), cancellationToken);
            return;
        }

        await store.UpdateConversationAsync(conversationId, "seleccionando_cantidad",
            new Dictionary<string, string?> { ["sauceId"] = sauceId.ToString(CultureInfo.InvariantCulture) }, cancellationToken);
        await SendAsync(conversationId, waId, new ButtonsWhatsAppMessage(
            "¿Cuántas unidades quieres agregar?",
            [new("qty:1", "1"), new("qty:2", "2"), new("qty:3", "3")]), cancellationToken);
    }

    private async Task AskQuantityAsync(long conversationId, string waId, string name, decimal price, CancellationToken cancellationToken) =>
        await SendAsync(conversationId, waId, new ButtonsWhatsAppMessage(
            $"{name}\nPrecio: ${price:N0}\n¿Cuántas unidades quieres agregar?",
            [new("qty:1", "1"), new("qty:2", "2"), new("qty:3", "3")]), cancellationToken);

    private async Task HandleQuantityAsync(ConversationRegistration registration, IncomingWhatsAppMessage incoming, string input, CancellationToken cancellationToken)
    {
        var quantityText = input.StartsWith("qty:", StringComparison.Ordinal) ? input[4..] : input;
        if (!int.TryParse(quantityText, out var quantity) || quantity is < 1 or > 20)
        {
            await SendAsync(registration.ConversationId, incoming.WaId,
                new TextWhatsAppMessage("Indica una cantidad entre 1 y 20."), cancellationToken);
            return;
        }

        var cart = await store.AddToCartAsync(registration.ConversationId, incoming, registration.Context, quantity, cancellationToken);
        await store.UpdateConversationAsync(registration.ConversationId, "revisando_carrito", new Dictionary<string, string?>(), cancellationToken);
        await SendAsync(registration.ConversationId, incoming.WaId, new ButtonsWhatsAppMessage(
            $"Producto agregado. Total actual: ${cart.Total:N0}",
            [new("menu", "Seguir comprando"), new("cart:show", "Ver pedido"), new("cart:finish", "Finalizar")]), cancellationToken);
    }

    private async Task ShowCartAsync(long conversationId, string waId, CancellationToken cancellationToken)
    {
        var cart = await store.GetCartAsync(conversationId, cancellationToken);
        if (cart is null || cart.Lines.Count == 0)
        {
            await SendAsync(conversationId, waId, new TextWhatsAppMessage("Tu carrito está vacío. Escribe *menú* para ver la carta."), cancellationToken);
            return;
        }

        var body = Shorten("Tu pedido:\n" + string.Join("\n", cart.Lines) + $"\n\nTotal: ${cart.Total:N0}", 1024);
        await store.UpdateConversationAsync(conversationId, "revisando_carrito", new Dictionary<string, string?>(), cancellationToken);
        await SendAsync(conversationId, waId, new ButtonsWhatsAppMessage(body,
            [new("menu", "Seguir comprando"), new("cart:finish", "Finalizar")]), cancellationToken);
    }

    private async Task HandleCartActionAsync(long conversationId, string waId, string input, CancellationToken cancellationToken)
    {
        if (input == "cart:finish")
        {
            await ShowPaymentMethodsAsync(conversationId, waId, cancellationToken);
        }
        else
        {
            await ShowCategoriesAsync(conversationId, waId, 0, cancellationToken);
        }
    }

    private async Task ShowPaymentMethodsAsync(long conversationId, string waId, CancellationToken cancellationToken)
    {
        var cart = await store.GetCartAsync(conversationId, cancellationToken);
        if (cart is null || cart.Lines.Count == 0)
        {
            await SendAsync(conversationId, waId, new TextWhatsAppMessage("Tu carrito está vacío. Escribe *menú* para comenzar."), cancellationToken);
            return;
        }

        await store.UpdateConversationAsync(conversationId, "seleccionando_pago", new Dictionary<string, string?>(), cancellationToken);
        await SendAsync(conversationId, waId, new ButtonsWhatsAppMessage(
            $"Total: ${cart.Total:N0}. Elige el medio de pago.",
            [new("pay:cash", "Efectivo"), new("pay:card", "Tarjeta"), new("cart:show", "Volver")]), cancellationToken);
    }

    private async Task HandlePaymentAsync(long conversationId, string waId, string input, CancellationToken cancellationToken)
    {
        if (input == "pay:cash")
        {
            var orderId = await store.ConfirmCashOrderAsync(conversationId, cancellationToken);
            if (orderId is null)
            {
                await SendAsync(conversationId, waId, new TextWhatsAppMessage("No encontré un pedido pendiente para confirmar."), cancellationToken);
                return;
            }

            await store.UpdateConversationAsync(conversationId, "finalizado", new Dictionary<string, string?>(), cancellationToken);
            await SendAsync(conversationId, waId,
                new TextWhatsAppMessage($"¡Pedido #{orderId} confirmado! El pago en efectivo queda pendiente para el momento de la entrega o retiro."), cancellationToken);
            return;
        }

        if (input == "pay:card")
        {
            await SendAsync(conversationId, waId,
                new TextWhatsAppMessage("El pago con tarjeta estará disponible cuando conectemos Mercado Pago. Puedes volver y elegir efectivo."), cancellationToken);
            return;
        }

        await ShowCartAsync(conversationId, waId, cancellationToken);
    }

    private async Task SendAsync(long conversationId, string waId, OutgoingWhatsAppMessage message, CancellationToken cancellationToken)
    {
        var content = message switch
        {
            TextWhatsAppMessage text => text.Body,
            ButtonsWhatsAppMessage buttons => buttons.Body,
            ListWhatsAppMessage list => list.Body,
            _ => string.Empty
        };
        var type = message switch
        {
            TextWhatsAppMessage => "texto",
            ButtonsWhatsAppMessage => "botones",
            ListWhatsAppMessage => "lista",
            _ => "desconocido"
        };

        try
        {
            var providerId = await sender.SendAsync(waId, message, cancellationToken);
            await store.RecordOutgoingAsync(conversationId, providerId, type, content, true, null, cancellationToken);
        }
        catch (Exception exception)
        {
            await store.RecordOutgoingAsync(conversationId, null, type, content, false, exception.Message, cancellationToken);
        }
    }

    private static IReadOnlyList<WhatsAppListRow> PageRows<T>(
        IReadOnlyList<T> source,
        int page,
        Func<T, WhatsAppListRow> map,
        Func<int, WhatsAppListRow> next)
    {
        const int pageSize = 9;
        var safePage = Math.Max(0, page);
        var rows = source.Skip(safePage * pageSize).Take(pageSize).Select(map).ToList();
        if ((safePage + 1) * pageSize < source.Count)
        {
            rows.Add(next(safePage + 1));
        }
        return rows;
    }

    private static bool TryParseId(string input, string prefix, out long id)
    {
        id = 0;
        return input.StartsWith(prefix, StringComparison.Ordinal) &&
               long.TryParse(input[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out id);
    }

    private static bool TryParseProductPage(string input, out long categoryId, out int page)
    {
        categoryId = 0;
        page = 0;
        var parts = input.Split(':');
        return parts.Length == 3 && parts[0] == "prodpage" &&
               long.TryParse(parts[1], out categoryId) && int.TryParse(parts[2], out page);
    }

    private static string Shorten(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
