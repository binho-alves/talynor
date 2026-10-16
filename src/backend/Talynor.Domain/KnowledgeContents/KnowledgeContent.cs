namespace Talynor.Domain.KnowledgeContents;

public sealed class KnowledgeContent
{
    private KnowledgeContent() { }

    public Guid Id { get; private set; }
    public Guid KnowledgeNodeId { get; private set; }
    public KnowledgeContentType Type { get; private set; }
    public string? Title { get; private set; }
    public string? Body { get; private set; }
    public string? Url { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static KnowledgeContent Create(Guid knowledgeNodeId, KnowledgeContentType type,
        string? title, string? body, string? url, DateTimeOffset createdAtUtc)
    {
        if (knowledgeNodeId == Guid.Empty) throw new ArgumentException("A node ID is required.", nameof(knowledgeNodeId));
        if (title is not null)
        {
            title = title.Trim();
            if (title.Length is 0 or > 200) throw new ArgumentException("Invalid title length.", nameof(title));
        }
        switch (type)
        {
            case KnowledgeContentType.Note:
                if (string.IsNullOrWhiteSpace(body) || body.Length > 4000 || url is not null)
                    throw new ArgumentException("A note requires only a nonblank bounded body.");
                break;
            case KnowledgeContentType.Link:
                if (body is not null) throw new ArgumentException("A link cannot contain a body.", nameof(body));
                url = ValidateUrl(url);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type));
        }
        var now = createdAtUtc.ToUniversalTime();
        return new() { Id = Guid.NewGuid(), KnowledgeNodeId = knowledgeNodeId, Type = type,
            Title = title, Body = body, Url = url, CreatedAtUtc = now, UpdatedAtUtc = now };
    }

    private static string ValidateUrl(string? value)
    {
        // Validate syntax only. Internal destinations are allowed; never resolve or fetch them.
        if (value is null || value.Any(char.IsControl) || value.Contains('\\'))
            throw new ArgumentException("Invalid link URL.", nameof(value));
        value = value.Trim();
        if (value.Length is 0 or > 2048 || value.Any(char.IsWhiteSpace) ||
            !(value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
              value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.IsWellFormedOriginalString() ||
            string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Invalid link URL.", nameof(value));
        var authority = value[(value.IndexOf("://", StringComparison.Ordinal) + 3)..].Split('/', '?', '#')[0];
        if (authority.Contains('@')) throw new ArgumentException("URL credentials are not allowed.", nameof(value));
        return value;
    }
}
