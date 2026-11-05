using System.Text.Json;

namespace Tuesday.Application.Views;

/// <summary>
/// Reads the column choices a saved view stores. Unknown or missing keys return null, and
/// the view then falls back to the first column of the right type.
/// </summary>
public static class ViewSettings
{
    public static Guid? Column(string? settingsJson, string key)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(settingsJson);
            if (document.RootElement.TryGetProperty(key, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                Guid.TryParse(value.GetString(), out var id))
            {
                return id;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    public static bool Flag(string? settingsJson, string key, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            return fallback;
        }

        try
        {
            using var document = JsonDocument.Parse(settingsJson);
            if (document.RootElement.TryGetProperty(key, out var value))
            {
                return value.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => fallback
                };
            }
        }
        catch (JsonException)
        {
            return fallback;
        }

        return fallback;
    }
}
