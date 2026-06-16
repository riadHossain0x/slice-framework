using FluentValidation;
using Slice.Application.Results;
using Slice.Core.Results;
using Slice.Mediator;

namespace Slice.Application.Behaviors;

/// <summary>
/// Runs all FluentValidation validators registered for the request. On failure, short-circuits
/// to a failed <see cref="Result"/> (when the response is a Result) or throws.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>, IHasPipelineOrder
    where TRequest : IRequest<TResponse>
{
    public int Order => PipelineOrder.Validation;

    public async Task<TResponse> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var validatorList = validators as IValidator<TRequest>[] ?? validators.ToArray();
        if (validatorList.Length == 0)
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(validatorList.Select(v => v.ValidateAsync(context, ct)));
        var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();

        if (failures.Count == 0)
            return await next();

        var details = failures
            .GroupBy(f => f.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray());

        var error = Error.Validation("Validation", "One or more validation errors occurred.", details);
        return ResultFactory.FailureOrThrow<TResponse>(error);
    }
}
