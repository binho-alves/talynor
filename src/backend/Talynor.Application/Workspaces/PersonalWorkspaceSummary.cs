namespace Talynor.Application.Workspaces;

public sealed record PersonalWorkspaceSummary(Guid Id, string OwnerUserId, DateTimeOffset CreatedAtUtc);
