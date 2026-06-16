namespace Slice.AspNetCore.ConditionalRequests;

/// <summary>
/// Opt-in marker for a response payload (DTO) that can supply a cheap, strong ETag validator without
/// hashing the body. Return a value that changes whenever the resource changes — typically the
/// aggregate's <c>ConcurrencyStamp</c> or a serialized <c>LastModificationTime</c>. Payloads that do
/// not implement this fall back to a content hash.
/// </summary>
public interface IHasResourceVersion
{
    string ResourceVersion { get; }
}
