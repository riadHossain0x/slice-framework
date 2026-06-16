using Slice.Core.DependencyInjection;
using Slice.ObjectMapping;
using Slice.Sample.Crm.Domain.Leads;
using Slice.Sample.Crm.ReadModels;

namespace Slice.Sample.Crm.Mapping;

/// <summary>Typed Lead → LeadDto mapper (hand-written here; Mapperly/AutoMapper would slot in identically).</summary>
public sealed class LeadDtoMapper : IObjectMapper<Lead, LeadDto>, ITransientDependency
{
    public LeadDto Map(Lead l) => new(
        l.Id, l.Name.DisplayName, l.Contact.Email, l.Contact.Phone, l.Status, l.Source, l.CreationTime, l.CreatorId,
        l.ConcurrencyStamp, l.LastModificationTime);
}
