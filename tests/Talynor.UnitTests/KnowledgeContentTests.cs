using Talynor.Domain.KnowledgeContents;

namespace Talynor.UnitTests;

public class KnowledgeContentTests
{
    private static KnowledgeContent Create(KnowledgeContentType type, string? title = null, string? body = null, string? url = null) =>
        KnowledgeContent.Create(Guid.NewGuid(), type, title, body, url, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Note_preserves_plain_text_trims_optional_title_and_normalizes_time()
    {
        var node = Guid.NewGuid();
        var time = new DateTimeOffset(2026, 10, 16, 12, 30, 0, TimeSpan.FromHours(-3));
        const string body = "  <b>literal</b>\n# plain text\t ";
        var note = KnowledgeContent.Create(node, KnowledgeContentType.Note, " Title ", body, null, time);
        Assert.NotEqual(Guid.Empty, note.Id);
        Assert.Equal(node, note.KnowledgeNodeId);
        Assert.Equal(KnowledgeContentType.Note, note.Type);
        Assert.Equal("Title", note.Title); Assert.Equal(body, note.Body); Assert.Null(note.Url);
        Assert.Equal(time.ToUniversalTime(), note.CreatedAtUtc);
        Assert.Equal(TimeSpan.Zero, note.CreatedAtUtc.Offset);
        Assert.Equal(note.CreatedAtUtc, note.UpdatedAtUtc);
        Assert.NotEqual(note.Id, Create(KnowledgeContentType.Note, body: body).Id);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" \n\t")]
    public void Note_requires_nonblank_body(string? body) =>
        Assert.ThrowsAny<ArgumentException>(() => Create(KnowledgeContentType.Note, body: body));

    [Theory]
    [InlineData("")] [InlineData("https://example.invalid")]
    public void Note_rejects_even_empty_url(string url) =>
        Assert.ThrowsAny<ArgumentException>(() => Create(KnowledgeContentType.Note, body: "note", url: url));

    [Fact]
    public void Text_limits_count_utf16_units_and_title_is_optional_but_not_blank()
    {
        Assert.Null(Create(KnowledgeContentType.Note, body: "note").Title);
        Assert.Equal(200, Create(KnowledgeContentType.Note, new string('t', 200), new string('b', 4000)).Title!.Length);
        Assert.ThrowsAny<ArgumentException>(() => Create(KnowledgeContentType.Note, new string('t', 201), "note"));
        Assert.ThrowsAny<ArgumentException>(() => Create(KnowledgeContentType.Note, " ", "note"));
        Assert.ThrowsAny<ArgumentException>(() => Create(KnowledgeContentType.Note, body: new string('b', 4001)));
        var unicode = string.Concat(Enumerable.Repeat("😀", 2000));
        Assert.Equal(unicode, Create(KnowledgeContentType.Note, body: unicode).Body);
        Assert.ThrowsAny<ArgumentException>(() => Create(KnowledgeContentType.Note, body: unicode + "x"));
    }

    [Theory]
    [InlineData("https://example.invalid/path?q=a%20b#section")]
    [InlineData("http://localhost:8080/path")]
    [InlineData("https://intranet/docs")]
    [InlineData("http://192.168.1.10:8080/path")]
    [InlineData("http://[::1]/path")]
    [InlineData("HTTPS://Example.Invalid/Case?Q=Value#Fragment")]
    public void Link_accepts_syntax_only_including_internal_hosts_without_rewriting(string url)
    {
        var link = Create(KnowledgeContentType.Link, url: "  " + url + "  ");
        Assert.Equal(url, link.Url); Assert.Null(link.Body); Assert.Null(link.Title);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" ")]
    [InlineData("/relative")] [InlineData("//example.invalid/path")]
    [InlineData("javascript:alert(1)")] [InlineData("data:text/plain,hello")]
    [InlineData("file:///C:/private.txt")] [InlineData("ftp://example.invalid/path")]
    [InlineData("https:/example.invalid")] [InlineData("https://")]
    [InlineData("https://user:password@example.invalid")]
    [InlineData("https://user@example.invalid")] [InlineData("https://@example.invalid")]
    [InlineData("https://example.invalid/\npath")] [InlineData("https://example.invalid/\tpath")]
    [InlineData("https://example.invalid/\0path")] [InlineData("\nhttps://example.invalid")]
    [InlineData("https://example.invalid\\@other.invalid/")]
    [InlineData("https://example.invalid/a b")]
    public void Link_rejects_invalid_or_active_url_forms(string? url) =>
        Assert.ThrowsAny<ArgumentException>(() => Create(KnowledgeContentType.Link, url: url));

    [Theory]
    [InlineData("")] [InlineData("body")]
    public void Link_rejects_body(string body) =>
        Assert.ThrowsAny<ArgumentException>(() => Create(KnowledgeContentType.Link, body: body, url: "https://example.invalid"));

    [Fact]
    public void Url_limit_is_enforced_after_outer_trim()
    {
        const string prefix = "https://example.invalid/";
        var url = prefix + new string('x', 2048 - prefix.Length);
        Assert.Equal(url, Create(KnowledgeContentType.Link, url: " " + url + " ").Url);
        Assert.ThrowsAny<ArgumentException>(() => Create(KnowledgeContentType.Link, url: url + "x"));
    }

    [Fact]
    public void Unknown_type_empty_parent_and_public_identity_mutation_are_rejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => Create((KnowledgeContentType)99, body: "note"));
        Assert.ThrowsAny<ArgumentException>(() => KnowledgeContent.Create(Guid.Empty, KnowledgeContentType.Note, null, "note", null, DateTimeOffset.UnixEpoch));
        foreach (var name in new[] { "Id", "KnowledgeNodeId", "Type", "CreatedAtUtc", "UpdatedAtUtc", "Title", "Body", "Url" })
            Assert.True(typeof(KnowledgeContent).GetProperty(name)!.SetMethod!.IsPrivate);
    }
}
