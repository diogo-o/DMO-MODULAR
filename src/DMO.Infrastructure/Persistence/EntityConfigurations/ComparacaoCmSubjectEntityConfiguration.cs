using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>comparacao_cm_subjects</c> table: one compared CM subject per
/// selected CM, identified by the NATURAL key <c>(comparacao_id, cm_id)</c>.
/// </summary>
/// <remarks>
/// The composite primary key IS the (comparacao_id, cm_id) uniqueness invariant — one subject per
/// selected CM inside one comparison event. <see cref="CmId"/> is a RESTRICT foreign key to the
/// EXACT existing <c>cm_contexts</c> row (the frozen CM identity of the production context): no
/// new CM identity, no reference/lot copy, no comparison-specific replacement. The CHECKs enforce
/// the closed two-value decision vocabulary, the put-aside justification rule and the
/// decision-state consistency; the <c>version</c> concurrency token guards measurement recording
/// and the FINAL decision.</remarks>
public sealed class ComparacaoCmSubjectEntityConfiguration : IEntityTypeConfiguration<ComparacaoCmSubjectEntity>
{
    /// <summary>Database name of the closed decision-vocabulary CHECK.</summary>
    public const string DecisionCheckConstraintName = "comparacao_cm_subjects_decision_check";

    /// <summary>Database name of the put-aside reason CHECK (validator backstop).</summary>
    public const string ReasonCheckConstraintName = "comparacao_cm_subjects_reason_check";

    /// <summary>Database name of the decision-state consistency CHECK.</summary>
    public const string DecisionStateCheckConstraintName = "comparacao_cm_subjects_decision_state_check";

    /// <summary>Database name of the comparison foreign key.</summary>
    public const string ComparacaoForeignKeyConstraintName = "FK_comparacao_cm_subjects_comparacoes_comparacao_id";

    /// <summary>Database name of the canonical CM-context foreign key (the frozen CM identity).</summary>
    public const string CmForeignKeyConstraintName = "FK_comparacao_cm_subjects_cm_contexts_cm_id";

    /// <summary>Database name of the selection-actor foreign key.</summary>
    public const string CreatedByForeignKeyConstraintName = "FK_comparacao_cm_subjects_users_created_by_user_id";

    /// <summary>Database name of the decision-actor foreign key.</summary>
    public const string DecidedByForeignKeyConstraintName = "FK_comparacao_cm_subjects_users_decided_by_user_id";

    /// <summary>Database name of the FK-supporting CM index.</summary>
    public const string CmIndexName = "IX_comparacao_cm_subjects_cm_id";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ComparacaoCmSubjectEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("comparacao_cm_subjects", table =>
        {
            table.HasCheckConstraint(
                DecisionCheckConstraintName,
                "decision IS NULL OR decision IN ('manter','colocar_de_parte')");
            table.HasCheckConstraint(
                ReasonCheckConstraintName,
                "decision IS NULL OR decision <> 'colocar_de_parte' OR (reason IS NOT NULL AND btrim(reason) <> '')");
            table.HasCheckConstraint(
                DecisionStateCheckConstraintName,
                "(decision IS NULL AND decided_by_user_id IS NULL AND decided_at IS NULL) OR " +
                "(decision IS NOT NULL AND decided_by_user_id IS NOT NULL AND decided_at IS NOT NULL)");
        });

        // The natural key (comparacao_id, cm_id) IS the primary key — the uniqueness invariant of
        // one subject per selected CM inside one comparison event (no surrogate subject identity).
        builder.HasKey(subject => new { subject.ComparacaoId, subject.CmId });

        builder.Property(subject => subject.ComparacaoId)
            .HasColumnName("comparacao_id")
            .IsRequired();

        builder.Property(subject => subject.CmId)
            .HasColumnName("cm_id")
            .IsRequired();

        builder.Property(subject => subject.CreatedByUserId)
            .HasColumnName("created_by_user_id")
            .IsRequired();

        builder.Property(subject => subject.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Property(subject => subject.Decision)
            .HasColumnName("decision");

        builder.Property(subject => subject.Reason)
            .HasColumnName("reason");

        builder.Property(subject => subject.DecidedByUserId)
            .HasColumnName("decided_by_user_id");

        builder.Property(subject => subject.DecidedAt)
            .HasColumnName("decided_at");

        builder.Property(subject => subject.Version)
            .HasColumnName("version")
            .IsRequired()
            .HasDefaultValue(1)
            .IsConcurrencyToken();

        builder.Property(subject => subject.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        builder.HasOne<ComparacaoEntity>()
            .WithMany()
            .HasForeignKey(subject => subject.ComparacaoId)
            .HasConstraintName(ComparacaoForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CmContextEntity>()
            .WithMany()
            .HasForeignKey(subject => subject.CmId)
            .HasConstraintName(CmForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(subject => subject.CreatedByUserId)
            .HasConstraintName(CreatedByForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(subject => subject.DecidedByUserId)
            .HasConstraintName(DecidedByForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(subject => subject.CmId)
            .HasDatabaseName(CmIndexName);
    }
}