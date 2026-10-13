using System.ComponentModel.DataAnnotations;

namespace Talynor.Api.Contracts;

public sealed class RegisterRequest : IValidatableObject
{
    public string? Email { get; init; }
    public string? Password { get; init; }

    internal static readonly string[] ValidationMessages =
    [
        "Informe um email válido.", "O email deve ter no máximo 256 unidades UTF-16.",
        "Informe uma senha não vazia.", "A senha deve ter no máximo 128 unidades UTF-16."
    ];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var email = Email?.Trim();
        if (string.IsNullOrEmpty(email) || !new EmailAddressAttribute().IsValid(email))
        {
            yield return new(ValidationMessages[0], [nameof(Email)]);
        }
        if (email?.Length > 256)
        {
            yield return new(ValidationMessages[1], [nameof(Email)]);
        }
        if (string.IsNullOrEmpty(Password))
        {
            yield return new(ValidationMessages[2], [nameof(Password)]);
        }
        if (Password?.Length > 128)
        {
            yield return new(ValidationMessages[3], [nameof(Password)]);
        }
    }
}
