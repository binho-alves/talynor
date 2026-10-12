using Talynor.Application.KnowledgeMaps;

namespace Talynor.UnitTests;

public class GetKnowledgeMapTests
{
    [Fact]
    public async Task Forwards_id_and_token_and_returns_existing_result()
    {
        var expected = new KnowledgeMapReadModel("software", "Software Development", "Demo", [], []);
        var reader = new FakeReader(expected);
        using var cancellation = new CancellationTokenSource();

        var result = await new GetKnowledgeMap(reader).ExecuteAsync("software", cancellation.Token);

        Assert.Equal("software", reader.MapId);
        Assert.Equal(cancellation.Token, reader.Token);
        Assert.Same(expected, result);
    }

    [Fact]
    public async Task Returns_absence_when_reader_has_no_map()
    {
        var reader = new FakeReader(null);
        var result = await new GetKnowledgeMap(reader).ExecuteAsync("unknown", CancellationToken.None);
        Assert.Null(result);
        Assert.Equal("unknown", reader.MapId);
    }

    private sealed class FakeReader(KnowledgeMapReadModel? result) : IKnowledgeMapReader
    {
        public string? MapId { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task<KnowledgeMapReadModel?> GetByIdAsync(string mapId, CancellationToken cancellationToken)
        {
            MapId = mapId;
            Token = cancellationToken;
            return Task.FromResult(result);
        }
    }
}
