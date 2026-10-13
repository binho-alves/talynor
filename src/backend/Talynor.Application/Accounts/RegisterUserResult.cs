namespace Talynor.Application.Accounts;

public enum RegisterUserStatus { Success, Invalid, Duplicate }

public sealed record RegisterUserResult(
    RegisterUserStatus Status,
    IReadOnlyDictionary<string, string[]>? Errors = null);
