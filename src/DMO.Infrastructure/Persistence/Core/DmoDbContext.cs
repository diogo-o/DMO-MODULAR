using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DMO.Infrastructure.Persistence;

/// <summary>
/// The single application persistence context.
/// </summary>
/// <remarks>
/// <para>
/// P1-T03 adds the Phase 1 persistence-foundation entities (the account and Template
/// foundation): <see cref="Templates"/>, <see cref="Users"/>, <see cref="AdminAccounts"/>
/// (Migration 001) and <see cref="TemplateModules"/> (Migration 002). Entity type
/// configurations live in <c>DMO.Infrastructure.Persistence.EntityConfigurations</c> and
/// mirror the migration constraints exactly.
/// </para>
/// <para>
/// There is exactly one database context by design. The accepted plan forbids multiple
/// database contexts; phases share the same backend.
/// </para>
/// <para>
/// No Settings/Audit/permission/capability/industrial tables are modelled in P1-T03 (they
/// are out of scope by the accepted plan).
/// </para>
/// </remarks>
public sealed class DmoDbContext : DbContext
{
    /// <summary>Creates the context.</summary>
    public DmoDbContext(DbContextOptions<DmoDbContext> options) : base(options)
    {
    }

    /// <summary>Templates (reusable access configuration; no active/inactive lifecycle).</summary>
    public DbSet<TemplateEntity> Templates => Set<TemplateEntity>();

    /// <summary>Application USER accounts; <c>company_number</c> is the canonical USER login identifier.</summary>
    public DbSet<UserEntity> Users => Set<UserEntity>();

    /// <summary>The single ADMIN account (DB-enforced singleton, CHECK + PK).</summary>
    public DbSet<AdminAccountEntity> AdminAccounts => Set<AdminAccountEntity>();

    /// <summary>Persisted Template → Module composition (Migration 002).</summary>
    public DbSet<TemplateModuleEntity> TemplateModules => Set<TemplateModuleEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DmoDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}