using System.ComponentModel.DataAnnotations;

namespace Talynor.Api.Contracts;

public sealed class LoginRequest : IValidatableObject
{
    public string? Email { get; init; }
    public string? Password { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var email = Email?.Trim();
        if (string.IsNullOrEmpty(email) || !new EmailAddressAttribute().IsValid(email))
            yield return new(RegisterRequest.ValidationMessages[0], [nameof(Email)]);
        if (email?.Length > 256)
            yield return new(RegisterRequest.ValidationMessages[1], [nameof(Email)]);
        if (string.IsNullOrEmpty(Password))
            yield return new(RegisterRequest.ValidationMessages[2], [nameof(Password)]);
        if (Password?.Length > 128)
            yield return new(RegisterRequest.ValidationMessages[3], [nameof(Password)]);
    }
}
