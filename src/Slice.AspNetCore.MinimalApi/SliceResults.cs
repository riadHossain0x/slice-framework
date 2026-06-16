using Microsoft.AspNetCore.Http;
using Slice.AspNetCore.Http;
using SliceResult = Slice.Core.Results.IResult;

namespace Slice.AspNetCore.MinimalApi;

/// <summary>
/// Maps a framework <see cref="Slice.Core.Results.IResult"/> to a minimal-API
/// <see cref="Microsoft.AspNetCore.Http.IResult"/> — the minimal-API counterpart of
/// <c>SliceController.ToActionResult</c>. Reuses <see cref="ProblemDetailsMapper"/> for failures so
/// controllers and minimal APIs produce identical status codes and ProblemDetails bodies.
/// </summary>
public static class SliceResults
{
    public static IResult ToHttpResult(SliceResult result)
    {
        if (result.IsSuccess)
        {
            var value = result.GetValue();
            return value is null ? TypedResults.NoContent() : TypedResults.Ok(value);
        }

        var error = result.Error!;
        return TypedResults.Problem(ProblemDetailsMapper.ToProblemDetails(error));
    }
}
