namespace Slice.Authentication;

/// <summary>Authentication options (bound from the "SliceAuth" configuration section).</summary>
public sealed class SliceAuthOptions
{
    /// <summary>EF connection string for the identity/OpenIddict store.</summary>
    public string ConnectionString { get; set; } = "Data Source=auth.db";

    /// <summary>Seed a demo admin (granted every declared permission) on startup.</summary>
    public bool SeedDemoAdmin { get; set; } = true;

    public string AdminEmail { get; set; } = "admin@slice";
    public string AdminPassword { get; set; } = "Admin123!";
    public string AdminRole { get; set; } = "admin";
}
