using KameronSushi.Application.WhatsApp;

namespace KameronSushi.Infrastructure.WhatsApp;

public static class TwilioMessageTextRenderer
{
    public static string Render(OutgoingWhatsAppMessage message) => message switch
    {
        TextWhatsAppMessage text => text.Body,
        ButtonsWhatsAppMessage buttons => RenderOptions(
            buttons.Body,
            buttons.Buttons.Select(button => (button.Title, (string?)null))),
        ListWhatsAppMessage list => RenderOptions(
            list.Body,
            list.Rows.Select(row => (row.Title, row.Description))),
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    };

    private static string RenderOptions(
        string body,
        IEnumerable<(string Title, string? Description)> options)
    {
        var lines = options.Select((option, index) =>
            option.Description is null
                ? $"{index + 1}. {option.Title}"
                : $"{index + 1}. {option.Title} — {option.Description}");
        return $"{body}\n\n{string.Join("\n", lines)}\n\nResponde con el número de una opción.";
    }
}
