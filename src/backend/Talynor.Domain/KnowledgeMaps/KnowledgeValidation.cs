namespace Talynor.Domain.KnowledgeMaps;

internal static class KnowledgeValidation
{
    internal static void Id(Guid id, string name)
    {
        if (id == Guid.Empty) throw new ArgumentException("An ID is required.", name);
    }

    internal static string Title(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        title = title.Trim();
        if (title.Length > 200) throw new ArgumentException("Title exceeds 200 UTF-16 units.", nameof(title));
        return title;
    }

    internal static void Description(string? description)
    {
        if (description?.Length > 4000)
            throw new ArgumentException("Description exceeds 4000 UTF-16 units.", nameof(description));
    }
}
