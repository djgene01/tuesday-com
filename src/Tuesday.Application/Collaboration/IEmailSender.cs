namespace Tuesday.Application.Collaboration;

/// <summary>
/// Outbound mail, behind a seam so development never needs an SMTP server and tests never
/// send anything.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default);
}

public sealed record EmailOptions
{
    public bool Enabled { get; init; }

    public string Host { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 25;

    public string? User { get; init; }

    public string? Password { get; init; }

    public bool UseStartTls { get; init; } = true;

    public string From { get; init; } = "no-reply@tuesday.local";
}
