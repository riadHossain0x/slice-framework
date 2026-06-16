using Slice.Application.UnitOfWork;
using Slice.Core.Results;
using Slice.Mediator;

namespace Slice.Application.Behaviors;

/// <summary>
/// For commands only: after the handler runs successfully, flushes every registered
/// <see cref="IUnitOfWork"/> (triggering audit + domain-event interceptors). A failed
/// <see cref="Result"/> (or an exception) leaves changes unsaved. Queries pass straight through.
/// </summary>
public sealed class UnitOfWorkBehavior<TRequest, TResponse>(IEnumerable<IUnitOfWork> unitsOfWork)
    : IPipelineBehavior<TRequest, TResponse>, IHasPipelineOrder
    where TRequest : IRequest<TResponse>
{
    public int Order => PipelineOrder.UnitOfWork;

    public async Task<TResponse> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is not ICommandBase)
            return await next();

        var response = await next();

        // Do not persist when the command reports a failed Result.
        if (response is IResult { IsSuccess: false })
            return response;

        foreach (var unit in unitsOfWork)
            await unit.SaveChangesAsync(ct);

        return response;
    }
}
