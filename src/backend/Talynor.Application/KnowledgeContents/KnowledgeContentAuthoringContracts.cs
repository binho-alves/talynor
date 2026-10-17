namespace Talynor.Application.KnowledgeContents;

public enum KnowledgeContentWriteStatus { Success, AuthenticationRequired, NotFound, InvalidInput }
public enum KnowledgeContentWriteOperation { Create, Update, Delete }
public sealed record KnowledgeContentWriteAttempt(KnowledgeContentWriteOperation Operation,
    Guid WorkspaceId, Guid MapId, Guid NodeId, Guid ContentId);
public sealed record CreateKnowledgeContentInput(string Type, string? Title, string? Body, string? Url,
    bool BodySpecified = false, bool UrlSpecified = false);
public sealed record UpdateKnowledgeContentInput(string? Title = null, string? Body = null, string? Url = null,
    bool ClearTitle = false, bool BodySpecified = false, bool UrlSpecified = false);
public sealed record CreateKnowledgeContentResult(KnowledgeContentWriteStatus Status, KnowledgeContentDetail? Content = null);
