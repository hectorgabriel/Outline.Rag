using System.ComponentModel.DataAnnotations;

namespace Outline.Rag.Api.Endpoints;

/// <summary>
/// Checks the DataAnnotations on a request body and answers 400 with the field errors. Applied per endpoint
/// rather than through AddValidation, whose source-generated validator walks every reachable type and throws on
/// the JsonElement in <see cref="ChatCompletionMessage"/>.
/// </summary>
internal sealed class ValidateRequest<TRequest> : IEndpointFilter where TRequest : class
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();
        if (request is null)
        {
            return next(context);
        }

        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true))
        {
            return next(context);
        }

        var errors = results
            .GroupBy(r => r.MemberNames.FirstOrDefault() ?? "")
            .ToDictionary(g => g.Key, g => g.Select(r => r.ErrorMessage ?? "Invalid value.").ToArray());
        return ValueTask.FromResult<object?>(TypedResults.ValidationProblem(errors));
    }
}
