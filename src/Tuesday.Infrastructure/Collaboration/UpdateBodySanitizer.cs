using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Ganss.Xss;
using Tuesday.Application.Collaboration;

namespace Tuesday.Infrastructure.Collaboration;

/// <summary>
/// Whitelist sanitiser for update bodies. The default HtmlSanitizer policy is wide; this
/// one is deliberately narrow — prose, lists, quotes, links and inline code. No images (so
/// a post cannot phone home), no style attributes, no block layout the app does not own.
/// </summary>
public sealed partial class UpdateBodySanitizer : IUpdateBodySanitizer
{
    private readonly HtmlSanitizer _sanitizer;

    public UpdateBodySanitizer()
    {
        _sanitizer = new HtmlSanitizer();

        _sanitizer.AllowedTags.Clear();
        foreach (var tag in AllowedTags)
        {
            _sanitizer.AllowedTags.Add(tag);
        }

        _sanitizer.AllowedAttributes.Clear();
        _sanitizer.AllowedAttributes.Add("href");
        _sanitizer.AllowedAttributes.Add("title");

        _sanitizer.AllowedSchemes.Clear();
        _sanitizer.AllowedSchemes.Add("http");
        _sanitizer.AllowedSchemes.Add("https");
        _sanitizer.AllowedSchemes.Add("mailto");

        _sanitizer.AllowedCssProperties.Clear();
        _sanitizer.AllowedAtRules.Clear();
        _sanitizer.KeepChildNodes = true;
    }

    private static readonly string[] AllowedTags =
    [
        "p", "br", "strong", "b", "em", "i", "u", "s", "del",
        "code", "pre", "blockquote",
        "ul", "ol", "li",
        "a", "h3", "h4"
    ];

    public SanitisedUpdate Sanitise(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new SanitisedUpdate(string.Empty, string.Empty);
        }

        // Script and style *contents* are not markup, they are code and CSS. The sanitizer
        // keeps the text of a removed element (so a <div> does not lose its words), which is
        // safe but noisy, so those two blocks are dropped whole before it runs.
        var withoutCode = ScriptOrStyle().Replace(input, " ");

        var html = _sanitizer.Sanitize(withoutCode);
        var text = ToPlainText(html);

        return new SanitisedUpdate(html.Trim(), text);
    }

    /// <summary>
    /// Runs on our own sanitised output, never on user input, so stripping the remaining
    /// tags cannot reintroduce anything: the dangerous markup is already gone.
    /// </summary>
    private static string ToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var spaced = BlockTag().Replace(html, " ");
        var stripped = Tag().Replace(spaced, string.Empty);
        var decoded = WebUtility.HtmlDecode(stripped);

        var builder = new StringBuilder(decoded.Length);
        var previousWasSpace = false;

        foreach (var character in decoded)
        {
            var isSpace = char.IsWhiteSpace(character);
            if (isSpace && previousWasSpace)
            {
                continue;
            }

            builder.Append(isSpace ? ' ' : character);
            previousWasSpace = isSpace;
        }

        return builder.ToString().Trim();
    }

    [GeneratedRegex(@"<\s*(script|style)\b[^>]*>[\s\S]*?<\s*/\s*\1\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"<\s*(br|/p|/div|/li|/blockquote|/h[1-6])\s*/?\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTag();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex Tag();
}
