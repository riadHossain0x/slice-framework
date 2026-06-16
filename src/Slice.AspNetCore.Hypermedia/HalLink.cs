namespace Slice.AspNetCore.Hypermedia;

/// <summary>
/// A single HAL link (a value in the resource's <c>_links</c> object). <see cref="Href"/> is the only
/// required field; <see cref="Method"/> is a pragmatic, widely-used extension (not part of the HAL spec)
/// that tells a client which verb the link affords. <see cref="Templated"/> marks a URI Template.
/// </summary>
public sealed record HalLink(string Href, string? Method = null, string? Title = null, bool Templated = false);

/// <summary>Well-known HAL media type and link relations.</summary>
public static class Hal
{
    /// <summary>The HAL+JSON media type. Responses are enriched only when the client accepts this.</summary>
    public const string MediaType = "application/hal+json";

    /// <summary>The reserved <c>self</c> relation (the canonical URI of the resource).</summary>
    public const string Self = "self";
}
