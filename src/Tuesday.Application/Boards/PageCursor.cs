using System.Globalization;
using System.Text;

namespace Tuesday.Application.Boards;

/// <summary>
/// Opaque page cursors. The value is the last row's database sequence, base64url-encoded so
/// clients treat it as a token rather than a number they might try to construct themselves.
/// </summary>
public static class PageCursor
{
    public static string Encode(long seq) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(seq.ToString(CultureInfo.InvariantCulture)))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    public static bool TryDecode(string? value, out long seq)
    {
        seq = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var padded = value.Replace('-', '+').Replace('_', '/');

        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
            case 1: return false;
        }

        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(padded));

            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out seq);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
