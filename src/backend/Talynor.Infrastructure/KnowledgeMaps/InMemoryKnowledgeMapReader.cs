using Talynor.Application.KnowledgeMaps;

namespace Talynor.Infrastructure.KnowledgeMaps;

// Demonstration content only; relationships do not establish domain semantics.
public sealed class InMemoryKnowledgeMapReader : IKnowledgeMapReader
{
    private static readonly Dictionary<string, KnowledgeMapReadModel> Maps = new(StringComparer.Ordinal)
    {
        ["software"] = new("software", "Software Development", "Um mapa para explorar desenvolvimento de software. Selecione C# para abrir seu submapa.",
            Array.AsReadOnly(new KnowledgeNodeReadModel[]
            {
                new("software-development", "Software Development", "Conceitos e práticas para construir software, conectando linguagem, ferramentas e arquitetura.", 35, 420, null),
                new("csharp", "C#", "Explore a linguagem C# e conceitos como LINQ, generics e programação assíncrona.", 60, 180, "csharp"),
                new("aspnet-core", "ASP.NET Core", "Uma visão dos conceitos de desenvolvimento de aplicações web com ASP.NET Core.", 25, 90, null),
                new("ef-core", "EF Core", "Conceitos de mapeamento entre objetos e dados, consultas e persistência.", 15, 60, null),
                new("architecture", "Architecture", "Organização de responsabilidades, dependências e decisões na construção de software.", 40, 90, null),
            }),
            Array.AsReadOnly(new KnowledgeEdgeReadModel[]
            {
                new("software-csharp", "software-development", "csharp"),
                new("software-aspnet", "software-development", "aspnet-core"),
                new("software-architecture", "software-development", "architecture"),
                new("csharp-ef", "csharp", "ef-core"),
                new("aspnet-ef", "aspnet-core", "ef-core"),
            })),
        ["csharp"] = new("csharp", "C#", "Explore conceitos da linguagem. Use o botão de voltar para retornar ao mapa anterior.",
            Array.AsReadOnly(new KnowledgeNodeReadModel[]
            {
                new("linq", "LINQ", "Consultas e transformações sobre sequências de dados usando recursos da linguagem.", 75, 60, null),
                new("generics", "Generics", "Tipos e métodos reutilizáveis que preservam a verificação de tipos.", 50, 35, null),
                new("async-await", "Async/Await", "Coordenação de operações assíncronas de maneira legível.", 30, 40, null),
                new("nullable", "Nullable", "Representação e tratamento de valores que podem estar ausentes.", 80, 20, null),
                new("collections", "Collections", "Estruturas para organizar e percorrer conjuntos de valores.", 65, 25, null),
            }),
            Array.AsReadOnly(new KnowledgeEdgeReadModel[]
            {
                new("linq-collections", "linq", "collections"),
                new("generics-collections", "generics", "collections"),
                new("generics-nullable", "generics", "nullable"),
                new("generics-async", "generics", "async-await"),
            })),
    };

    public Task<KnowledgeMapReadModel?> GetByIdAsync(string mapId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Maps.GetValueOrDefault(mapId));
    }
}
