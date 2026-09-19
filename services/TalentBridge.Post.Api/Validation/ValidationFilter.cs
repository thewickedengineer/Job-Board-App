using System.Text.Json;
using FluentValidation;

namespace TalentBridge.Post.Api.Validation;

/// <summary>
/// Runs the registered <see cref="IValidator{T}"/> for the endpoint's body
/// argument and short-circuits with the RFC 9457 <c>ValidationProblemDetails</c>
/// shape from CLAUDE.md §6. Keys are camelCase so the Angular client can map
/// them onto form controls by name with no translation table.
/// </summary>
public sealed class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var validator = context.HttpContext.RequestServices.GetService<IValidator<T>>();
        var argument = context.Arguments.OfType<T>().FirstOrDefault();

        if (validator is not null && argument is not null)
        {
            var result = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);
            if (!result.IsValid)
            {
                var errors = result.Errors
                    .GroupBy(e => ToCamelCase(e.PropertyName), StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray(), StringComparer.Ordinal);

                return TypedResults.ValidationProblem(errors);
            }
        }

        return await next(context);
    }

    // "SalaryMax" → "salaryMax", "Address.PostalCode" → "address.postalCode"
    private static string ToCamelCase(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
}

public static class ValidationFilterExtensions
{
    public static RouteHandlerBuilder WithValidation<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.AddEndpointFilter<ValidationFilter<T>>();
}
