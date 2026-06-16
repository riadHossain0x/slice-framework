using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Slice.AspNetCore;
using Slice.Authorization;
using Slice.EntityFrameworkCore;
using Slice.EventBus;
using Slice.Mediator.Default;
using Slice.Modularity;
using Slice.MultiTenancy;
using ModuleName.Domain;
using ModuleName.Persistence;

namespace ModuleName;

/// <summary>
/// The ModuleName bounded context. Self-registers its handlers, validators, repositories and
/// DbContext. Add it to your host's root module via <c>[DependsOn(typeof(ModuleNameModule))]</c>.
/// </summary>
[DependsOn(
    typeof(SliceAspNetCoreModule),
    typeof(SliceEntityFrameworkCoreModule),
    typeof(SliceMultiTenancyModule),
    typeof(SliceAuthorizationModule))]
public sealed class ModuleNameModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var assembly = typeof(ModuleNameModule).Assembly;

        services.AddRequestHandlers(assembly);
        services.AddDomainEventHandlers(assembly);
        services.AddDistributedEventHandlers(assembly);
        services.AddValidatorsFromAssembly(assembly);
        services.AddSliceConventions(assembly);   // permission providers + marker services

        services.AddSliceDbContext<ModuleNameDbContext>(options =>
            options.UseSqlite(context.Configuration.GetConnectionString("ModuleName") ?? "Data Source=modulename.db"));
        services.AddScoped<IItemRepository, ItemRepository>();
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        using var scope = context.ServiceProvider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ModuleNameDbContext>().Database.EnsureCreatedAsync();
    }
}
