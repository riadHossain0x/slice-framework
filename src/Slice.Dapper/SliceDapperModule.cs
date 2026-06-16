using Microsoft.Extensions.DependencyInjection;
using Slice.EntityFrameworkCore;
using Slice.Modularity;

namespace Slice.Dapper;

/// <summary>
/// Dapper integration: raw-SQL access that shares the EF connection + transaction (unit of work).
/// </summary>
[DependsOn(typeof(SliceEntityFrameworkCoreModule))]
public sealed class SliceDapperModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
        => context.Services.AddScoped(typeof(IDapperExecutor<>), typeof(DapperExecutor<>));
}
