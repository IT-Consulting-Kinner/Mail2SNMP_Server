using System.ComponentModel.DataAnnotations;

namespace Mail2SNMP.Api.Filters;

public class ValidationFilter<T> : IEndpointFilter where T : class
{
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
