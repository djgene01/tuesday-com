namespace Tuesday.Application.Collaboration;

/// <summary>The sanitised form of one submitted update body.</summary>
public sealed record SanitisedUpdate(string Html, string Text)
{
    /// <summary>True when nothing readable is left, e.g. the body was only markup.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);
}

/// <summary>
/// One place where untrusted prose becomes markup we are willing to render. Nothing else
/// in the product may write <c>Update.BodyHtml</c>, so the allow-list cannot be bypassed.
/// </summary>
public interface IUpdateBodySanitizer
{
    SanitisedUpdate Sanitise(string? input);
}
