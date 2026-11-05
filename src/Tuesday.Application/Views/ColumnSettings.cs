using System.Text.Json;

namespace Tuesday.Application.Views;

/// <summary>
/// Column options are stored as JSON on the column itself. These readers are the only
/// place that knows the shape, so a view never parses settings on its own.
/// </summary>
public static class ColumnSettings
{
    public static IReadOnlyList<string> Labels(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(settingsJson);
            if (!document.RootElement.TryGetProperty("labels", out var labels) ||
                labels.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return labels.EnumerateArray()
                .Where(label => label.ValueKind == JsonValueKind.String)
                .Select(label => label.GetString()!)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
