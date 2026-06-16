using Microsoft.AspNetCore.Identity;
using Slice.Domain;

namespace Slice.Authentication;

/// <summary>Application user (Guid-keyed ASP.NET Identity), with an extra-properties JSON column.</summary>
public sealed class SliceUser : IdentityUser<Guid>, IHasExtraProperties
{
    public ExtraPropertyDictionary ExtraProperties { get; private set; } = new();
}

/// <summary>Application role; carries <c>permission</c> role-claims that flow into issued tokens.</summary>
public sealed class SliceRole : IdentityRole<Guid>, IHasExtraProperties
{
    public ExtraPropertyDictionary ExtraProperties { get; private set; } = new();
}
