using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Slice.AspNetCore.Http;
using Slice.Core.Results;
using Slice.Mediator;

namespace Slice.AspNetCore.Mvc;

/// <summary>
/// Base class for thin, slice-local controllers. Dispatches requests through <see cref="ISender"/>
/// and maps the <see cref="Result"/>/<see cref="Result{T}"/> outcome to an <see cref="IActionResult"/>.
/// </summary>
[ApiController]
public abstract class SliceController : ControllerBase
{
    private ISender? _sender;
    protected ISender Sender => _sender ??= HttpContext.RequestServices.GetRequiredService<ISender>();

    /// <summary>Send a request and map its response to an HTTP result.</summary>
    protected async Task<IActionResult> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        var response = await Sender.SendAsync(request, ct);
        return response is IResult result ? ToActionResult(result) : Ok(response);
    }

    protected IActionResult ToActionResult(IResult result)
    {
        if (result.IsSuccess)
        {
            var value = result.GetValue();
            return value is null ? NoContent() : Ok(value);
        }

        var error = result.Error!;
        return StatusCode(ProblemDetailsMapper.StatusFor(error.Type), ProblemDetailsMapper.ToProblemDetails(error));
    }
}
