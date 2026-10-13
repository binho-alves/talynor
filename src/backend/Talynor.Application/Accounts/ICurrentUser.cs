namespace Talynor.Application.Accounts;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    string? UserId { get; }
}
