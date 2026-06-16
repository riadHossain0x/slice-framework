using Slice.Domain.Entities;
using Slice.Domain.Exceptions;
using Slice.Domain.MultiTenancy;
using Slice.Sample.Crm.Domain.Leads.Events;

namespace Slice.Sample.Crm.Domain.Leads;

public enum LeadStatus { New = 0, Contacted = 5, Qualified = 10, Converted = 15, Lost = 20 }
public enum LeadSource { Manual = 0, Web = 5, Referral = 10, Portal = 15 }

/// <summary>
/// Lead aggregate root. State changes go through business methods; raises distributed events.
/// (Mirrors the team's real ABP Lead aggregate, trimmed for the framework sample.)
/// </summary>
public sealed class Lead : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    private Lead() { } // ORM

    public Lead(Guid id, Guid? tenantId, FullName name, ContactInfo contact, LeadSource source)
        : base(id)
    {
        TenantId = tenantId;
        Name = name;
        Contact = contact;
        Source = source;
        Status = LeadStatus.New;
        AddDistributedEvent(new LeadCreatedEto(id, contact.Email));
        AddDomainEvent(new LeadCreatedDomainEvent(id, name.DisplayName));
    }

    public Guid? TenantId { get; private set; }
    public FullName Name { get; private set; } = null!;
    public ContactInfo Contact { get; private set; } = null!;
    public LeadSource Source { get; private set; }
    public LeadStatus Status { get; private set; }

    public void ChangeStatus(LeadStatus newStatus)
    {
        if (Status == newStatus)
            throw new BusinessRuleException($"Lead is already in status '{newStatus}'.");
        Status = newStatus;
    }

    public void UpdateContact(ContactInfo contact) => Contact = contact;
}
