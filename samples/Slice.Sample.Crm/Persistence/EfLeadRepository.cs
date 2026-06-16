using Slice.EntityFrameworkCore;
using Slice.Sample.Crm.Domain.Leads;

namespace Slice.Sample.Crm.Persistence;

public sealed class EfLeadRepository(CrmDbContext db) : EfRepository<CrmDbContext, Lead, Guid>(db), ILeadRepository;
