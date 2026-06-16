using Slice.Core.Results;
using Slice.Mediator;

namespace Slice.Application;

/// <summary>Non-generic marker so the unit-of-work behavior can detect commands.</summary>
public interface ICommandBase;

/// <summary>A state-changing request. Goes through the unit-of-work + domain-event pipeline.</summary>
public interface ICommand<TResponse> : IRequest<TResponse>, ICommandBase;

/// <summary>A state-changing request with no return value.</summary>
public interface ICommand : IRequest<Result>, ICommandBase;

/// <summary>A read-only request. Bypasses the unit-of-work.</summary>
public interface IQuery<TResponse> : IRequest<TResponse>;

public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>;

public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>;
