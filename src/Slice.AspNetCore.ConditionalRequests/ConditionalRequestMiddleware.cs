using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Slice.AspNetCore.ConditionalRequests;

/// <summary>Tuning for <see cref="ConditionalRequestMiddleware"/>.</summary>
public sealed class ConditionalRequestOptions
{
    /// <summary>
    /// Largest response (bytes) the middleware will hash for an ETag. Larger bodies are streamed through
    /// without a computed validator, capping the buffering cost. Responses that already carry an ETag
    /// (e.g. a version validator set by a result filter) are honoured regardless of size.
    /// </summary>
    public long MaxBodyBytes { get; set; } = 1024 * 1024;
}

/// <summary>
/// Adds HTTP conditional-request semantics to the pipeline:
/// <list type="bullet">
/// <item>Safe reads (GET/HEAD) get a strong <c>ETag</c> — a version validator if one was set upstream,
/// otherwise a content hash — and a matching <c>If-None-Match</c> short-circuits to <c>304</c>.</item>
/// <item>Writes that fail an <c>If-Match</c> precondition (a <see cref="PreconditionFailedException"/> or
/// EF's <see cref="DbUpdateConcurrencyException"/>) are mapped to <c>412</c>.</item>
/// </list>
/// Register it <em>after</em> <c>UseSliceExceptionHandling</c> so it maps the concurrency exception
/// before the generic 500 handler sees it.
/// </summary>
public sealed class ConditionalRequestMiddleware(RequestDelegate next, IOptions<ConditionalRequestOptions> options)
{
    private readonly ConditionalRequestOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            await InvokeWriteAsync(context);
            return;
        }

        await InvokeReadAsync(context);
    }

    private async Task InvokeWriteAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (ex is PreconditionFailedException or DbUpdateConcurrencyException && !context.Response.HasStarted)
        {
            await WritePreconditionFailedAsync(context);
        }
    }

    private async Task InvokeReadAsync(HttpContext context)
    {
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        var cacheable = context.Response.StatusCode == StatusCodes.Status200OK
                        && buffer.Length > 0
                        && !context.Response.Headers.ContainsKey(HeaderNames.SetCookie);

        if (cacheable)
        {
            var etag = context.Response.Headers.ETag.ToString();
            var hasVersionETag = !string.IsNullOrEmpty(etag);

            if (!hasVersionETag && buffer.Length <= _options.MaxBodyBytes)
            {
                etag = ComputeContentTag(buffer);
                context.Response.Headers.ETag = etag;
            }

            if (!string.IsNullOrEmpty(etag))
            {
                AppendVaryAccept(context.Response);

                if (IfNoneMatch(context.Request, etag))
                {
                    context.Response.StatusCode = StatusCodes.Status304NotModified;
                    context.Response.ContentLength = null;
                    context.Response.Headers.Remove(HeaderNames.ContentType);
                    return;
                }
            }
        }

        buffer.Position = 0;
        context.Response.ContentLength = buffer.Length;
        await buffer.CopyToAsync(originalBody);
    }

    private static async Task WritePreconditionFailedAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status412PreconditionFailed;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status412PreconditionFailed,
            Title = "The resource was modified by another request.",
            Extensions = { ["code"] = "ConditionalRequest:PreconditionFailed" }
        };
        await context.Response.WriteAsJsonAsync(problem, problem.GetType());
    }

    private static string ComputeContentTag(MemoryStream buffer)
    {
        var hash = SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        return $"\"{Convert.ToBase64String(hash)}\"";
    }

    private static void AppendVaryAccept(HttpResponse response)
    {
        if (!response.Headers.Vary.Any(v => string.Equals(v, HeaderNames.Accept, StringComparison.OrdinalIgnoreCase)))
            response.Headers.Append(HeaderNames.Vary, HeaderNames.Accept);
    }

    private static bool IfNoneMatch(HttpRequest request, string etag)
    {
        if (request.Headers.IfNoneMatch.Count == 0)
            return false;

        foreach (var raw in request.Headers.IfNoneMatch)
        {
            if (raw is null)
                continue;

            foreach (var token in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (token == "*" || ETagsEqual(token, etag))
                    return true;
            }
        }

        return false;
    }

    // Weak comparison per RFC 9110 §13.1.2 (If-None-Match): the W/ prefix is ignored.
    private static bool ETagsEqual(string a, string b) => Strip(a) == Strip(b);

    private static string Strip(string value)
        => value.StartsWith("W/", StringComparison.Ordinal) ? value[2..].Trim() : value.Trim();
}

public static class ConditionalRequestMiddlewareExtensions
{
    /// <summary>Adds the conditional-request middleware (ETag/304 + If-Match/412).</summary>
    public static IApplicationBuilder UseSliceConditionalRequests(this IApplicationBuilder app)
        => app.UseMiddleware<ConditionalRequestMiddleware>();
}
