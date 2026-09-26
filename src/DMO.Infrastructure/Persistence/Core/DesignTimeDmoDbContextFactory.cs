using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DMO.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by the EF Core tooling (<c>dotnet ef</c>) to build the context
/// without executing the Web host.
/// </summary>
/// <remarks>
/// <para>
/// Tooling-only; never registered in the runtime service container. The connection string is
/// read from the <c>Database__ConnectionString</c> environment variable (the same key the
/// runtime binds); when absent a credential-free placeholder is used because model building
/// and migration generation do not connect to a database.
/// </para>
/// <para>
/// This file exists so <c>dotnet ef migrations add</c> targets <c>DMO.Infrastructure</c>
/// directly, without starting the Web host (whose eager configuration validation would fail
/// without secrets).
/// </para>
/// </remarks>
public sealed class DesignTimeDmoDbContextFactory : IDesignTimeDbContextFactory<DmoDbContext>
{
    /// <summary>
    /// Environment-variable form of <c>Database:ConnectionString</c> (matches the runtime binding).
    /// </summary>
    public const string ConnectionStringEnvironmentVariable = "Database__ConnectionString";

    /// <summary>Placeholder used only when no connection string is configured.</summary>
    public const string PlaceholderConnectionString =
        "Host=localhost;Port=5432;Database=dmo_design_time;Username=design;Password=design";

    /// <inheritdoc />
    public DmoDbContext CreateDbContext(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
            ?? PlaceholderConnectionString;

        var options = new DbContextOptionsBuilder<DmoDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(
                typeof(DmoDbContext).Assembly.GetName().Name))
            .Options;

        return new DmoDbContext(options);
    }
}