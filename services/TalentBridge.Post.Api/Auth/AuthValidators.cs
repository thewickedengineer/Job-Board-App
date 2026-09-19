using FluentValidation;
using TalentBridge.Post.Api.Validation;

namespace TalentBridge.Post.Api.Auth;

public sealed class SignupRequestValidator : AbstractValidator<SignupRequest>
{
    // Per wireframe 1.1: personal mailbox domains are rejected server-side so the
    // organization on the account is a real employer.
    private static readonly HashSet<string> PersonalDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "googlemail.com", "outlook.com", "hotmail.com", "live.com", "msn.com",
        "yahoo.com", "yahoo.ca", "yahoo.co.uk", "icloud.com", "me.com", "mac.com", "aol.com",
        "proton.me", "protonmail.com", "mail.com", "gmx.com", "yandex.com", "zoho.com",
    };

    public SignupRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Work email is required.")
            .MaximumLength(320).WithMessage("Email must be at most 320 characters.")
            .MustBeEmailAddress().WithMessage("Enter a valid email address.")
            .Must(NotBePersonalDomain).WithMessage("Use your work email — personal mailbox domains aren't accepted.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(12).WithMessage("Password must be at least 12 characters.")
            .MaximumLength(128).WithMessage("Password must be at most 128 characters.")
            .Must(p => p.Any(char.IsUpper) && p.Any(char.IsLower) && p.Any(char.IsDigit))
                .WithMessage("Password must include upper and lower case letters and a number.");

        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required.")
            .Length(2, 120).WithMessage("Full name must be 2–120 characters.");

        RuleFor(x => x.Organization)
            .NotEmpty().WithMessage("Company / organization is required.")
            .Length(2, 120).WithMessage("Company name must be at least 2 characters.");
    }

    private static bool NotBePersonalDomain(string? email)
    {
        if (email is null)
        {
            return true;
        }

        var at = email.LastIndexOf('@');
        return at < 0 || !PersonalDomains.Contains(email[(at + 1)..].Trim());
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    // Deliberately minimal: credential checking is server-side only and the
    // failure message must never reveal which half was wrong.
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.").MaximumLength(320);
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.").MaximumLength(128);
    }
}
