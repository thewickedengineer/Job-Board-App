using System.Text.RegularExpressions;
using FluentValidation;

namespace TalentBridge.Post.Api.Validation;

/// <summary>
/// FluentValidation's built-in <c>EmailAddress()</c> only checks for a single
/// "@", so <c>careers@northline</c> passes. Deliverable mail needs a dotted
/// domain; this is the rule both signup and postings use.
/// </summary>
public static partial class EmailRule
{
    public static IRuleBuilderOptions<T, string?> MustBeEmailAddress<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(value => value is not null && value.Length <= 320 && Pattern().IsMatch(value));

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s.]+$")]
    private static partial Regex Pattern();
}
