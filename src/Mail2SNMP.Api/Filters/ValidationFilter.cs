using System.ComponentModel.DataAnnotations;

namespace Mail2SNMP.Api.Filters;

/// <summary>
/// Minimal-API endpoint filter that runs <see cref="System.ComponentModel.DataAnnotations"/> validation
/// on the first request argument of type <typeparamref name="T"/> before the endpoint handler executes.
/// Short-circuits with a 400 response when the body is missing or invalid, so handlers can assume a
/// validated model.
/// </summary>
/// <typeparam name="T">The request DTO type to locate among the endpoint arguments and validate.</typeparam>
public class ValidationFilter<T> : IEndpointFilter where T : class
{
    /// <summary>
    /// Validates the <typeparamref name="T"/> argument and either short-circuits with a problem response
    /// or invokes the next filter/handler.
    /// </summary>
    /// <param name="context">The endpoint filter context whose arguments are searched for an instance of <typeparamref name="T"/>.</param>
    /// <param name="next">The next delegate in the filter pipeline, invoked only when validation passes.</param>
    /// <returns>
    /// A <see cref="Results.BadRequest(object)"/> result when no <typeparamref name="T"/> argument is present;
    /// a <c>Results.ValidationProblem</c> (RFC 7807) result, keyed by member name, when validation fails;
    /// otherwise the result of <paramref name="next"/>.
    /// </returns>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var arg = context.Arguments.OfType<T>().FirstOrDefault();
        if (arg is null)
            return Results.BadRequest(new { error = "Request body is required." });

        var results = new List<ValidationResult>();
        var validationContext = new ValidationContext(arg);
        if (!Validator.TryValidateObject(arg, validationContext, results, validateAllProperties: true))
        {
            var errors = results
                .GroupBy(r => r.MemberNames.FirstOrDefault() ?? string.Empty)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(r => r.ErrorMessage!).ToArray());

            return Results.ValidationProblem(errors);
        }

        return await next(context);
    }
}
