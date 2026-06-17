using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Slice.AspNetCore;
using Slice.AspNetCore.ConditionalRequests;
using Slice.AspNetCore.Hypermedia;
using Slice.Authorization;
using Slice.EntityFrameworkCore;
using Slice.Mediator.Default;
using Slice.Modularity;
using SliceMinimalApp.Domain;
using SliceMinimalApp.Persistence;

namespace SliceMinimalApp;

/// <summary>
/// Root module. Self-registers handlers, validators, link contributors, permission providers, the
/// DbContext and repository. Endpoints live in feature folders as <c>ISliceEndpoint</c>s and are
/// discovered by <c>MapSliceEndpoints</c> in Program.cs — adding a slice needs no edits here.
/// </summary>
[DependsOn(
    typeof(SliceAspNetCoreModule),
    typeof(SliceEntityFrameworkCoreModule),
    typeof(SliceAuthorizationModule),
    typeof(SliceHypermediaModule),
    typeof(SliceConditionalRequestsModule))]
public sealed class AppModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var assembly = typeof(AppModule).Assembly;

        services.AddSliceMediator();
        services.AddRequestHandlers(assembly);
        services.AddValidatorsFromAssembly(assembly);
        services.AddSliceConventions(assembly);             // permission providers
        services.AddResourceLinkContributors(assembly);     // HAL link contributors

        services.AddSliceDbContext<NotesDbContext>(options =>
            options.UseSqlite(context.Configuration.GetConnectionString("Notes") ?? "Data Source=notes.db"));
        services.AddScoped<INoteRepository, EfNoteRepository>();
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        using var scope = context.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        await db.Database.EnsureCreatedAsync();
    }
}
