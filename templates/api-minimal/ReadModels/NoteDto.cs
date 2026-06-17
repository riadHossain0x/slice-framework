using System.Text.Json.Serialization;
using Slice.AspNetCore.ConditionalRequests;

namespace SliceMinimalApp.ReadModels;

/// <summary>
/// Note read model. Exposes <c>ConcurrencyStamp</c> as the resource version so GETs get a cheap strong
/// ETag (no body hashing) and clients can echo it back as <c>If-Match</c> on a write.
/// </summary>
public sealed record NoteDto(
    Guid Id,
    string Title,
    string Body,
    DateTime CreationTime,
    string ConcurrencyStamp) : IHasResourceVersion
{
    [JsonIgnore] public string ResourceVersion => ConcurrencyStamp;
}
