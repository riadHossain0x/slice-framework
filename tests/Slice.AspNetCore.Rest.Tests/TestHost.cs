using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Slice.AspNetCore.ConditionalRequests;
using Slice.AspNetCore.Hypermedia;
using Slice.AspNetCore.MinimalApi;
using Slice.Authorization;
using Slice.Core.Results;

namespace Slice.AspNetCore.Rest.Tests;

// ── Test resource + persistence ──────────────────────────────────────────────────────────────────

public sealed class Widget
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Widget>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
        });
}

public sealed record WidgetDto(Guid Id, string Name, string ConcurrencyStamp) : IHasResourceVersion
{
    [JsonIgnore] public string ResourceVersion => ConcurrencyStamp;
}

// ── Hypermedia + permissions ─────────────────────────────────────────────────────────────────────

public sealed class WidgetLinks : IResourceLinkContributor<WidgetDto>
{
    public const string EditPermission = "widget.edit";

    public async Task ContributeAsync(WidgetDto widget, LinkBuilder links, CancellationToken ct)
    {
        links.EmbeddedRel = "widgets";
        links.Self("Get", "Widgets", new { id = widget.Id });
        links.Add("list", "List", "Widgets");
        links.AddRoute("audit", "WidgetAudit", new { id = widget.Id });           // named, parameterized route
        links.AddHref("docs", "https://docs.example/widgets", templated: false);  // literal / external href
        await links.AddIfGranted(EditPermission, "update", "ChangeName", "Widgets", new { id = widget.Id }, "PATCH", ct);
    }
}

/// <summary>Test permission checker: grants whatever the request lists in the <c>X-Granted</c> header.</summary>
public sealed class HeaderPermissionChecker(IHttpContextAccessor accessor) : IPermissionChecker
{
    public Task<bool> IsGrantedAsync(string permission, CancellationToken ct = default)
    {
        var header = accessor.HttpContext?.Request.Headers["X-Granted"].ToString() ?? "";
        var granted = header.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return Task.FromResult(granted.Contains(permission, StringComparer.OrdinalIgnoreCase));
    }
}

// ── Controller ───────────────────────────────────────────────────────────────────────────────────

public sealed record ChangeNameRequest(string Name);

[ApiController]
public sealed class WidgetsController(TestDbContext db) : ControllerBase
{
    [HttpGet("api/widgets/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var widget = await db.Widgets.FindAsync([id], ct);
        return widget is null ? NotFound() : Ok(ToDto(widget));
    }

    [HttpGet("api/widgets/{id:guid}/audit", Name = "WidgetAudit")]
    public IActionResult Audit(Guid id) => Ok(new { id, entries = Array.Empty<string>() });

    [HttpGet("api/widgets")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var widgets = await db.Widgets.AsNoTracking().ToListAsync(ct);
        return Ok(widgets.Select(ToDto).ToList());
    }

    [HttpPatch("api/widgets/{id:guid}/name")]
    public async Task<IActionResult> ChangeName(Guid id, [FromBody] ChangeNameRequest body, CancellationToken ct)
    {
        var widget = await db.Widgets.FindAsync([id], ct);
        if (widget is null)
            return NotFound();

        widget.Name = body.Name;
        widget.ConcurrencyStamp = Guid.NewGuid().ToString("N");   // roll the version on write
        db.Entry(widget).UseIfMatch(HttpContext.GetIfMatch());    // enforce the client's precondition
        await db.SaveChangesAsync(ct);                            // stale If-Match → DbUpdateConcurrencyException → 412

        return Ok(ToDto(widget));
    }

    private static WidgetDto ToDto(Widget w) => new(w.Id, w.Name, w.ConcurrencyStamp);
}

// ── Minimal-API surface (exercises Slice.AspNetCore.MinimalApi) ────────────────────────────────────
// Endpoints return the framework's Result<T> directly; SliceResultEndpointFilter (attached by
// MapSliceEndpoints) maps it, and the group's HAL + version-ETag filters apply.

public sealed record CreateWidgetBody(string Name);

public sealed class WidgetMinimalEndpoints : ISliceEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup("/min/widgets");

        widgets.MapGet("/{id:guid}", async (Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var w = await db.Widgets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return w is null
                ? Result<WidgetDto>.Failure(Error.NotFound("Widget:NotFound", $"Widget '{id}' was not found."))
                : Result<WidgetDto>.Success(new WidgetDto(w.Id, w.Name, w.ConcurrencyStamp));
        });

        // A success result whose value is null → 204 No Content.
        widgets.MapGet("/none", () => Result<WidgetDto?>.Success(null));

        // A Forbidden result (what a denied [SlicePermission] produces in the pipeline) → 403.
        widgets.MapGet("/forbidden", () => Result<WidgetDto>.Failure(Error.Forbidden("X:Forbidden", "nope")));

        // A Validation result → 400 with per-field details.
        widgets.MapPost("/", (CreateWidgetBody body) => string.IsNullOrEmpty(body.Name)
            ? Result<Guid>.Failure(Error.Validation("X:Invalid", "Validation failed.",
                new Dictionary<string, string[]> { ["Name"] = ["Name is required."] }))
            : Result<Guid>.Success(Guid.NewGuid()));
    }
}

// ── Self-hosted Kestrel fixture ──────────────────────────────────────────────────────────────────

/// <summary>
/// Boots a real Kestrel host wired with both libraries' filters/middleware and an EF (SQLite) backing
/// store, then exposes an <see cref="HttpClient"/>. End-to-end proof that HAL enrichment, ETag/304 and
/// If-Match/412 work over the wire.
/// </summary>
public sealed class TestHost : IAsyncLifetime
{
    private WebApplication _app = null!;
    private SqliteConnection _connection = null!;

    public HttpClient Client { get; private set; } = null!;
    public Guid WidgetId { get; private set; }

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<IPermissionChecker, HeaderPermissionChecker>();
        builder.Services.AddDbContext<TestDbContext>(o => o.UseSqlite(_connection));
        builder.Services.AddTransient<IResourceLinkContributor<WidgetDto>, WidgetLinks>();

        builder.Services.AddScoped<HalResourceFilter>();
        builder.Services.AddScoped<ResourceVersionResultFilter>();
        builder.Services.AddOptions<ConditionalRequestOptions>();
        builder.Services.AddControllers(options =>
        {
            options.Filters.AddService<ResourceVersionResultFilter>();
            options.Filters.AddService<HalResourceFilter>();
        }).AddApplicationPart(typeof(WidgetsController).Assembly);

        _app = builder.Build();
        _app.UseSliceConditionalRequests();
        _app.MapControllers();
        _app.MapSliceEndpoints(typeof(WidgetMinimalEndpoints).Assembly, g => g.AddHal().AddResourceVersion());

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await db.Database.EnsureCreatedAsync();
            var widget = new Widget { Id = Guid.NewGuid(), Name = "Acme" };
            db.Widgets.Add(widget);
            await db.SaveChangesAsync();
            WidgetId = widget.Id;
        }

        await _app.StartAsync();
        Client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
