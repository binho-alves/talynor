import type { KnowledgeMap, MapId } from '../types/knowledge'

// Connections illustrate navigation, not a final relationship taxonomy.
export const knowledgeMaps: Record<MapId, KnowledgeMap> = {
  software: {
    title: 'Software Development',
    description: 'Um mapa para explorar desenvolvimento de software. Selecione C# para abrir seu submapa.',
    nodes: [
      {
        id: 'software-development',
        type: 'knowledge',
        position: { x: 0, y: 210 },
        data: {
          title: 'Software Development',
          description: 'Conceitos e práticas para construir software, conectando linguagem, ferramentas e arquitetura.',
          progress: 35,
          studyMinutes: 420,
        },
      },
      {
        id: 'csharp',
        type: 'knowledge',
        position: { x: 350, y: 0 },
        data: {
          title: 'C#',
          description: 'Explore a linguagem C# e conceitos como LINQ, generics e programação assíncrona.',
          progress: 60,
          studyMinutes: 180,
          submapId: 'csharp',
        },
      },
      {
        id: 'aspnet-core',
        type: 'knowledge',
        position: { x: 350, y: 160 },
        data: {
          title: 'ASP.NET Core',
          description: 'Uma visão dos conceitos de desenvolvimento de aplicações web com ASP.NET Core.',
          progress: 25,
          studyMinutes: 90,
        },
      },
      {
        id: 'ef-core',
        type: 'knowledge',
        position: { x: 700, y: 160 },
        data: {
          title: 'EF Core',
          description: 'Conceitos de mapeamento entre objetos e dados, consultas e persistência.',
          progress: 15,
          studyMinutes: 60,
        },
      },
      {
        id: 'architecture',
        type: 'knowledge',
        position: { x: 350, y: 340 },
        data: {
          title: 'Architecture',
          description: 'Organização de responsabilidades, dependências e decisões na construção de software.',
          progress: 40,
          studyMinutes: 90,
        },
      },
    ],
    edges: [
      { id: 'software-csharp', source: 'software-development', target: 'csharp', type: 'smoothstep' },
      { id: 'software-aspnet', source: 'software-development', target: 'aspnet-core', type: 'smoothstep' },
      { id: 'software-architecture', source: 'software-development', target: 'architecture', type: 'smoothstep' },
      { id: 'csharp-ef', source: 'csharp', target: 'ef-core', type: 'smoothstep' },
      { id: 'aspnet-ef', source: 'aspnet-core', target: 'ef-core', type: 'smoothstep' },
    ],
  },
  csharp: {
    title: 'C#',
    description: 'Explore conceitos da linguagem. Use o botão de voltar para retornar ao mapa anterior.',
    nodes: [
      {
        id: 'linq',
        type: 'knowledge',
        position: { x: 0, y: 0 },
        data: {
          title: 'LINQ',
          description: 'Consultas e transformações sobre sequências de dados usando recursos da linguagem.',
          progress: 75,
          studyMinutes: 60,
        },
      },
      {
        id: 'generics',
        type: 'knowledge',
        position: { x: 0, y: 180 },
        data: {
          title: 'Generics',
          description: 'Tipos e métodos reutilizáveis que preservam a verificação de tipos.',
          progress: 50,
          studyMinutes: 35,
        },
      },
      {
        id: 'async-await',
        type: 'knowledge',
        position: { x: 350, y: 360 },
        data: {
          title: 'Async/Await',
          description: 'Coordenação de operações assíncronas de maneira legível.',
          progress: 30,
          studyMinutes: 40,
        },
      },
      {
        id: 'nullable',
        type: 'knowledge',
        position: { x: 350, y: 180 },
        data: {
          title: 'Nullable',
          description: 'Representação e tratamento de valores que podem estar ausentes.',
          progress: 80,
          studyMinutes: 20,
        },
      },
      {
        id: 'collections',
        type: 'knowledge',
        position: { x: 350, y: 0 },
        data: {
          title: 'Collections',
          description: 'Estruturas para organizar e percorrer conjuntos de valores.',
          progress: 65,
          studyMinutes: 25,
        },
      },
    ],
    edges: [
      { id: 'linq-collections', source: 'linq', target: 'collections', type: 'smoothstep' },
      { id: 'generics-collections', source: 'generics', target: 'collections', type: 'smoothstep' },
      { id: 'generics-nullable', source: 'generics', target: 'nullable', type: 'smoothstep' },
      { id: 'generics-async', source: 'generics', target: 'async-await', type: 'smoothstep' },
    ],
  },
}
