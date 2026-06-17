using System.Text.Json;
using System.Xml.Linq;

namespace Slice.Templates.Tests;

/// <summary>
/// Static (no-build) guardrails for the `dotnet new` templates in <c>templates/</c>: every template has a
/// valid manifest, identities/short-names are unique, the packaging project ships each template, runnable
/// templates carry a nuget.config, and template projects reference the framework as NuGet packages (with
/// the replaceable <c>SLICE_VERSION</c> token) rather than reaching into the monorepo <c>src/</c>.
/// The full pack → scaffold → build path is exercised separately by eng/smoke-templates.sh.
/// </summary>
public sealed class TemplateManifestTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string TemplatesDir = Path.Combine(RepoRoot, "templates");

    /// <summary>The shortName every shipped template is expected to expose.</summary>
    private static readonly string[] ExpectedShortNames =
    [
        "slice-api", "slice-api-minimal", "slice-module", "slice-monolith",
        "slice-worker", "slice-tenant-api", "slice-feature",
    ];


    public static TheoryData<string> ManifestPaths()
    {
        var data = new TheoryData<string>();
        foreach (var manifest in Directory.EnumerateFiles(TemplatesDir, "template.json", SearchOption.AllDirectories))
            if (manifest.Replace('\\', '/').EndsWith(".template.config/template.json"))
                data.Add(manifest);
        return data;
    }

    [Theory]
    [MemberData(nameof(ManifestPaths))]
    public void Manifest_is_valid_json_with_required_fields(string manifestPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;

        foreach (var field in new[] { "identity", "shortName", "sourceName", "name" })
        {
            Assert.True(root.TryGetProperty(field, out var value), $"{manifestPath}: missing '{field}'");
            Assert.False(string.IsNullOrWhiteSpace(value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText()),
                $"{manifestPath}: '{field}' is empty");
        }
    }

    [Fact]
    public void Identities_and_short_names_are_unique()
    {
        var manifests = ReadAllManifests();
        var dupIdentity = manifests.GroupBy(m => m.Identity).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        var dupShort = manifests.GroupBy(m => m.ShortName).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        Assert.True(dupIdentity.Count == 0, "Duplicate template identity: " + string.Join(", ", dupIdentity));
        Assert.True(dupShort.Count == 0, "Duplicate template shortName: " + string.Join(", ", dupShort));
    }

    [Fact]
    public void All_expected_short_names_are_present()
    {
        var actual = ReadAllManifests().Select(m => m.ShortName).ToHashSet();
        var missing = ExpectedShortNames.Where(s => !actual.Contains(s)).ToList();
        Assert.True(missing.Count == 0, "Missing template shortName(s): " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_template_directory_is_packaged_by_Slice_Templates_csproj()
    {
        var packaged = PackagedTemplateDirs();
        var templateDirs = Directory.EnumerateDirectories(TemplatesDir)
            .Where(d => File.Exists(Path.Combine(d, ".template.config", "template.json")))
            .Select(d => Path.GetFileName(d)!)
            .ToList();

        var unpackaged = templateDirs.Where(d => !packaged.Contains(d)).ToList();
        Assert.True(unpackaged.Count == 0,
            "Template folders not included by Slice.Templates.csproj <Content>: " + string.Join(", ", unpackaged));
    }

    [Fact]
    public void Templates_with_projects_ship_a_nuget_config()
    {
        // Any template that contains a .csproj references Slice.* packages, so it must ship a nuget.config
        // (the feed is needed until Slice.* is on nuget.org). Item templates (no project) are exempt.
        var missing = Directory.EnumerateDirectories(TemplatesDir)
            .Where(d => File.Exists(Path.Combine(d, ".template.config", "template.json")))
            .Where(d => Directory.EnumerateFiles(d, "*.csproj", SearchOption.AllDirectories)
                .Any(p => !p.Replace('\\', '/').Contains("/bin/") && !p.Replace('\\', '/').Contains("/obj/")))
            .Where(d => !File.Exists(Path.Combine(d, "nuget.config")))
            .Select(d => Path.GetFileName(d)!)
            .ToList();
        Assert.True(missing.Count == 0, "Templates with projects but no nuget.config: " + string.Join(", ", missing));
    }

    public static TheoryData<string> TemplateCsprojPaths()
    {
        var data = new TheoryData<string>();
        foreach (var csproj in Directory.EnumerateFiles(TemplatesDir, "*.csproj", SearchOption.AllDirectories))
        {
            var norm = csproj.Replace('\\', '/');
            if (norm.Contains("/bin/") || norm.Contains("/obj/")) continue;
            if (norm.EndsWith("Slice.Templates.csproj")) continue;   // the packaging project itself
            data.Add(csproj);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(TemplateCsprojPaths))]
    public void Template_projects_use_package_references_not_src_project_references(string csprojPath)
    {
        var doc = XDocument.Load(csprojPath);

        // No ProjectReference may reach into the monorepo src/ — templates are package-based.
        foreach (var pr in doc.Descendants("ProjectReference"))
        {
            var include = (pr.Attribute("Include")?.Value ?? "").Replace('\\', '/');
            Assert.DoesNotContain("src/", include);
        }

        // Every Slice.* PackageReference must carry the replaceable version token.
        foreach (var pkg in doc.Descendants("PackageReference"))
        {
            var include = pkg.Attribute("Include")?.Value ?? "";
            if (!include.StartsWith("Slice.")) continue;
            var version = pkg.Attribute("Version")?.Value;
            Assert.True(version == "SLICE_VERSION",
                $"{Path.GetFileName(csprojPath)}: {include} should use Version=\"SLICE_VERSION\" (was '{version ?? "<none>"}').");
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed record Manifest(string Identity, string ShortName, string Path);

    private static List<Manifest> ReadAllManifests()
    {
        var list = new List<Manifest>();
        foreach (var path in Directory.EnumerateFiles(TemplatesDir, "template.json", SearchOption.AllDirectories))
        {
            if (!path.Replace('\\', '/').EndsWith(".template.config/template.json")) continue;
            using var doc = JsonDocument.Parse(File.ReadAllText(path),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            list.Add(new Manifest(
                root.GetProperty("identity").GetString()!,
                root.GetProperty("shortName").GetString()!,
                path));
        }
        return list;
    }

    private static HashSet<string> PackagedTemplateDirs()
    {
        var csproj = XDocument.Load(Path.Combine(TemplatesDir, "Slice.Templates.csproj"));
        var dirs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var content in csproj.Descendants("Content"))
        {
            var include = (content.Attribute("Include")?.Value ?? "").Replace('\\', '/');
            var slash = include.IndexOf('/');
            if (slash > 0) dirs.Add(include[..slash]);   // "api-minimal/**/*" -> "api-minimal"
        }
        return dirs;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Slice.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root (Slice.slnx).");
    }
}
