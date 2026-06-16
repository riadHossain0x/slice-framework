using System.Reflection;
using Slice.Application.Results;
using Slice.Core.Results;
using Slice.Mediator;

namespace Slice.Authorization;

/// <summary>
/// Enforces <see cref="SlicePermissionAttribute"/>s declared on the request type. On the first
/// missing permission, short-circuits to <see cref="Error.Forbidden"/> (when the response is a
/// Result) or throws. Requests with no permission attribute pass through.
/// </summary>
public sealed class AuthorizationBehavior<TRequest, TResponse>(IPermissionChecker permissionChecker)
    : IPipelineBehavior<TRequest, TResponse>, IHasPipelineOrder
    where TRequest : IRequest<TResponse>
{
    public int Order => PipelineOrder.Authorization;

    private static readonly string[] Required =
        typeof(TRequest).GetCustomAttributes<SlicePermissionAttribute>(inherit: true)
            .Select(a => a.Permission)
            .Distinct()
            .ToArray();

    public async Task<TResponse> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        foreach (var permission in Required)
        {
            if (!await permissionChecker.IsGrantedAsync(permission, ct))
            {
                var error = Error.Forbidden("Authorization:Forbidden", $"Permission '{permission}' is required.");
                return ResultFactory.FailureOrThrow<TResponse>(error);
            }
        }

        return await next();
    }
}
