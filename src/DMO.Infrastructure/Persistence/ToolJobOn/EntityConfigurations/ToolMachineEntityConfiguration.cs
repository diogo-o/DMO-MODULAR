using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>tool_machines</c> table: Tool-owned machine compatibility.
/// </summary>
/// <remarks>
/// Authority: P2-T04 contract §3.2, §4.2 (<c>tool_machines_tool_machine_key</c>), §4.3 (RESTRICT FK)
/// and §4.5 (the machine-filter index). The "one or more machines" rule is enforced by the create
/// validator and proven by test; authority fixes no trigger and the accepted foundation has none.
/// </remarks>
public sealed class ToolMachineEntityConfiguration : IEntityTypeConfiguration<ToolMachineEntity>
{
    /// <summary>Database name of the one-row-per-machine unique index.</summary>
    public const string ToolMachineUniqueConstraintName = "tool_machines_tool_machine_key";

    /// <summary>Database name of the machine-filter index.</summary>
    public const string MachineIndexName = "tool_machines_machine_idx";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ToolMachineEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tool_machines", table =>
        {
            table.HasCheckConstraint(
                "tool_machines_machine_check",
                "machine IN ('B1','B2','B3','C1','C2','C3')");
        });

        builder.HasKey(machine => machine.ToolMachineId);
        builder.Property(machine => machine.ToolMachineId)
            .HasColumnName("tool_machine_id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(machine => machine.ToolId)
            .HasColumnName("tool_id")
            .IsRequired();

        // Every foreign key in this schema is ON DELETE RESTRICT: the schema never cascade-deletes
        // operational history.
        builder.HasOne(machine => machine.Tool)
            .WithMany(tool => tool.Machines)
            .HasForeignKey(machine => machine.ToolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(machine => machine.Machine)
            .HasColumnName("machine")
            .IsRequired();

        builder.HasIndex(machine => new { machine.ToolId, machine.Machine })
            .IsUnique()
            .HasDatabaseName(ToolMachineUniqueConstraintName);

        builder.HasIndex(machine => machine.Machine)
            .HasDatabaseName(MachineIndexName);
    }
}
