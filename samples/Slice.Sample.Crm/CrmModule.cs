using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Slice.AspNetCore;
using Slice.AspNetCore.ConditionalRequests;
using Slice.AspNetCore.Hypermedia;
using Slice.Authentication;
using Slice.Authorization;
using Slice.BackgroundJobs;
using Slice.BackgroundWorkers;
using Slice.BlobStoring;
using Slice.DistributedLocking;
using Slice.Caching;
using Slice.Emailing;
using Slice.EntityFrameworkCore;
using Slice.Features;
using Slice.EventBus;
using Slice.Localization;
using Slice.Management;
using Slice.Mediator.Default;
using Slice.Modularity;
using Slice.MultiTenancy;
using Slice.ObjectMapping;
using Slice.Settings;
using Slice.VirtualFileSystem;
using Slice.Sample.Crm.Domain.Leads;
using Slice.Sample.Crm.Persistence;

namespace Slice.Sample.Crm;

/// <summary>
/// The CRM bounded context. Self-registers its handlers, validators, repositories and DbContext,
/// and chooses the default mediator engine. Adding a feature folder requires no edits here.
/// </summary>
[DependsOn(
    typeof(SliceAspNetCoreModule),
    typeof(SliceHypermediaModule),
    typeof(SliceConditionalRequestsModule),
    typeof(SliceEntityFrameworkCoreModule),
    typeof(SliceMultiTenancyModule),
    typeof(SliceAuthorizationModule),
    typeof(SliceAuthenticationModule),
    typeof(SliceSettingsModule),
    typeof(SliceLocalizationModule),
    typeof(SliceBackgroundJobsModule),
    typeof(SliceCachingModule),
    typeof(SliceBlobStoringModule),
    typeof(SliceEmailingModule),
    typeof(SliceFeaturesModule),
    typeof(SliceDistributedLockingModule),
    typeof(SliceBackgroundWorkersModule),
    typeof(SliceObjectMappingModule),
    typeof(SliceVirtualFileSystemModule),
    typeof(SliceManagementModule))]
public sealed class CrmModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var assembly = typeof(CrmModule).Assembly;

        services.AddSliceMediator();                  // default engine
        services.AddRequestHandlers(assembly);        // command/query handlers
        services.AddDomainEventHandlers(assembly);    // local domain-event handlers
        services.AddDistributedEventHandlers(assembly); // outbox/integration handlers
        services.AddBackgroundJobHandlers(assembly);    // background job handlers
        services.AddValidatorsFromAssembly(assembly); // FluentValidation validators
        services.AddSliceConventions(assembly);       // permission providers + other marker services
        services.AddResourceLinkContributors(assembly); // HAL link contributors

        services.AddSliceDbContext<CrmDbContext>(options =>
            options.UseSqlite(context.Configuration.GetConnectionString("Crm") ?? "Data Source=crm.db"));

        services.AddScoped<ILeadRepository, EfLeadRepository>();

        // Identity/OpenIddict store (host picks the EF provider).
        services.AddSliceAuthStore(options =>
            options.UseSqlite(context.Configuration.GetConnectionString("Auth") ?? "Data Source=auth.db"));

        // Management store: DB-backed permission grants, tenants, setting/feature values.
        services.AddSliceManagementStore(options =>
            options.UseSqlite(context.Configuration.GetConnectionString("Management") ?? "Data Source=mgmt.db"));

        // Virtual file system: embedded resources from this assembly.
        services.ConfigureVirtualFileSystem(vfs => vfs.AddEmbedded<CrmModule>());
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        using var scope = context.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        await db.Database.EnsureCreatedAsync();
    }
}
