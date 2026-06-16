using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Slice.AspNetCore.ConditionalRequests;

/// <summary>
/// Thrown by a write handler when a client's <c>If-Match</c> precondition cannot be satisfied. The
/// <see cref="ConditionalRequestMiddleware"/> maps it (and EF's <c>DbUpdateConcurrencyException</c>) to
/// <c>412 Precondition Failed</c>.
/// </summary>
public sealed class PreconditionFailedException(string? message = null)
    : Exception(message ?? "The resource was modified by another request.");

/// <summary>
/// Helpers for reading conditional-request headers and applying them to EF Core's optimistic-concurrency
/// token (<c>ConcurrencyStamp</c>).
/// </summary>
public static class ConditionalRequest
{
    /// <summary>The default property used as the concurrency token validator.</summary>
    public const string ConcurrencyStampProperty = "ConcurrencyStamp";

    /// <summary>
    /// Returns the (unquoted) value of the request's <c>If-Match</c> header, or <c>null</c> when absent.
    /// Pass this into your command so the handler can enforce it via <see cref="UseIfMatch{TEntity}"/>.
    /// </summary>
    public static string? GetIfMatch(this HttpContext http)
    {
        var header = http.Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(header))
            return null;

        var first = header.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return first is null ? null : Unquote(first);
    }

    /// <summary>
    /// Sets a strong <c>ETag</c> response header from a resource version. Call before the body is written
    /// (e.g. from a result filter). The version is wrapped as a strong entity-tag.
    /// </summary>
    public static void SetETag(this HttpContext http, string version)
        => http.Response.Headers.ETag = $"\"{version}\"";

    /// <summary>
    /// Applies an <c>If-Match</c> precondition to a tracked entity by overriding the original value of its
    /// concurrency token. EF then emits <c>WHERE ConcurrencyStamp = @ifMatch</c> on the UPDATE; a stale
    /// value matches zero rows and raises <c>DbUpdateConcurrencyException</c>, which the middleware maps to
    /// 412. A <c>null</c>/empty <paramref name="ifMatch"/> is a no-op (no precondition supplied).
    /// </summary>
    public static void UseIfMatch<TEntity>(this EntityEntry<TEntity> entry, string? ifMatch) where TEntity : class
    {
        if (string.IsNullOrEmpty(ifMatch))
            return;

        entry.Property(ConcurrencyStampProperty).OriginalValue = Unquote(ifMatch);
    }

    private static string Unquote(string value)
    {
        var token = value.Trim();
        if (token.StartsWith("W/", StringComparison.Ordinal))
            token = token[2..].Trim();
        if (token.Length >= 2 && token[0] == '"' && token[^1] == '"')
            token = token[1..^1];
        return token;
    }
}
