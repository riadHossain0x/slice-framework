using System.Text.Json.Serialization;
using Slice.AspNetCore.ConditionalRequests;
using Slice.Sample.Crm.Domain.Leads;

namespace Slice.Sample.Crm.ReadModels;

/// <summary>
/// Lead read model. Exposes the aggregate's <c>ConcurrencyStamp</c> as the resource version, so GETs get
/// a cheap strong ETag (no body hashing) and clients can send it back as <c>If-Match</c> on a write.
/// </summary>
public sealed record LeadDto(
    Guid Id,
    string FullName,
    string? Email,
    string? Phone,
    LeadStatus Status,
    LeadSource Source,
    DateTime CreationTime,
    Guid? CreatorId,
    string ConcurrencyStamp,
    DateTime? LastModificationTime) : IHasResourceVersion
{
    [JsonIgnore] public string ResourceVersion => ConcurrencyStamp;
}
