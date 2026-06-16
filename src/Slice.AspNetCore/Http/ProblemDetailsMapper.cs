using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Slice.Core.Results;

namespace Slice.AspNetCore.Http;

/// <summary>Maps a framework <see cref="Error"/> to an HTTP status + (Validation)ProblemDetails.</summary>
public static class ProblemDetailsMapper
{
    public static int StatusFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        _ => StatusCodes.Status500InternalServerError
    };

    public static ProblemDetails ToProblemDetails(Error error)
    {
        var status = StatusFor(error.Type);

        if (error.Type == ErrorType.Validation && error.Details is { Count: > 0 })
        {
            return new ValidationProblemDetails(error.Details.ToDictionary(kv => kv.Key, kv => kv.Value))
            {
                Status = status,
                Title = error.Message,
                Extensions = { ["code"] = error.Code }
            };
        }

        return new ProblemDetails
        {
            Status = status,
            Title = error.Message,
            Extensions = { ["code"] = error.Code }
        };
    }
}
