using KameronSushi.Application.WhatsApp;

namespace KameronSushi.Infrastructure.WhatsApp;

public static class TwilioMessageTextRenderer
{
    private const string CartHint = "\n\n🛒 Escribe *carrito* en cualquier momento para ver tu pedido.";

    public static string Render(OutgoingWhatsAppMessage message)
    {
        var rendered = message switch
        {
            TextWhatsAppMessage text => text.Body,
            ButtonsWhatsAppMessage buttons => RenderOptions(
                buttons.Body,
                buttons.Buttons.Select(button => (button.Title, (string?)null, (string?)null))),
            ListWhatsAppMessage list => RenderOptions(
                list.Body,
                list.Rows.Select(row => (row.Title, row.Description, row.GroupTitle))),
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        };

        return HasFinished(message) ? rendered : rendered + CartHint;
    }

    private static bool HasFinished(OutgoingWhatsAppMessage message) =>
        message is TextWhatsAppMessage text &&
        (text.Body.StartsWith("¡Pedido #", StringComparison.Ordinal) ||
         text.Body.StartsWith("En este momento el local está cerrado", StringComparison.Ordinal));

    private static string RenderOptions(
        string body,
        IEnumerable<(string Title, string? Description, string? GroupTitle)> options)
    {
        var lines = new List<string>();
        string? currentGroup = null;
        foreach (var (option, index) in options.Select((value, index) => (value, index)))
        {
            if (option.GroupTitle is not null && option.GroupTitle != currentGroup)
            {
                if (lines.Count > 0)
                {
                    lines.Add(string.Empty);
                }
                lines.Add($"*{option.GroupTitle}*");
                currentGroup = option.GroupTitle;
            }

            lines.Add(option.Description is null
                ? $"{index + 1}. {option.Title}"
                : $"{index + 1}. {option.Title} — {option.Description}");
        }

        return $"{body}\n\n{string.Join("\n", lines)}\n\nResponde con el número de una opción.";
    }
}
