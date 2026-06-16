using Slice.Domain.Repositories;

namespace Slice.Sample.Crm.Domain.Leads;

/// <summary>Aggregate repository for <see cref="Lead"/>. (In-memory in the sample; EF Core in real apps.)</summary>
public interface ILeadRepository : IRepository<Lead, Guid>;
