using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>peso_review_decisions</c> table (P2-T06 contract §6.1/§7, exact).
/// </summary>
/// <remarks>
/// Maps the closed decision/prior-status CHECKs, the reason rule (validator + CHECK backstop:
/// <c>aprovado</c> never carries a reason; <c>nao_aprovado</c>/<c>reaberto</c> always do), the
/// version-at-decision CHECK, the two RESTRICT foreign keys (<c>pesos</c>, <c>users</c>) and the
/// two index-backed reads (per-Peso trail and decision-time history ordering).
/// </remarks>
public sealed class PesoReviewDecisionEntityConfiguration : IEntityTypeConfiguration<PesoReviewDecisionEntity>
{
    /// <summary>Database name of the closed decision-vocabulary CHECK.</summary>
    public const string DecisionCheckConstraintName = "peso_review_decisions_decision_check";

    /// <summary>Database name of the closed prior-status CHECK.</summary>
    public const string PriorStatusCheckConstraintName = "peso_review_decisions_prior_status_check";

    /// <summary>Database name of the reason-required CHECK (validator backstop).</summary>
    public const string ReasonRequiredCheckConstraintName = "peso_review_decisions_reason_required_check";

    /// <summary>Database name of the version-at-decision CHECK.</summary>
    public const string VersionCheckConstraintName = "peso_review_decisions_version_check";

    /// <summary>Database name of the Peso foreign key.</summary>
    public const string PesoForeignKeyConstraintName = "FK_peso_review_decisions_pesos_peso_id";

    /// <summary>Database name of the actor foreign key.</summary>
    public const string DecidedByForeignKeyConstraintName = "FK_peso_review_decisions_users_decided_by_user_id";

    /// <summary>Database name of the per-Peso trail index.</summary>
    public const string PesoIndexName = "IX_peso_review_decisions_peso_id";

    /// <summary>Database name of the decision-time history index.</summary>
    public const string DecidedAtIndexName = "IX_peso_review_decisions_decided_at";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PesoReviewDecisionEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("peso_review_decisions", table =>
        {
            table.HasCheckConstraint(DecisionCheckConstraintName, "decision IN ('aprovado','nao_aprovado','reaberto')");
            table.HasCheckConstraint(PriorStatusCheckConstraintName, "prior_status IN ('pendente','aprovado','nao_aprovado')");
            table.HasCheckConstraint(
                ReasonRequiredCheckConstraintName,
                "(decision = 'aprovado' AND reason IS NULL) OR (decision <> 'aprovado' AND btrim(reason) <> '')");
            table.HasCheckConstraint(VersionCheckConstraintName, "pesos_version_at_decision >= 1");
        });

        builder.HasKey(decision => decision.PesoReviewDecisionId);
        builder.Property(decision => decision.PesoReviewDecisionId)
            .HasColumnName("peso_review_decision_id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(decision => decision.PesoId)
            .HasColumnName("peso_id")
            .IsRequired();

        builder.Property(decision => decision.Decision)
            .HasColumnName("decision")
            .IsRequired();

        builder.Property(decision => decision.DecidedByUserId)
            .HasColumnName("decided_by_user_id")
            .IsRequired();

        builder.Property(decision => decision.DecidedAt)
            .HasColumnName("decided_at")
            .IsRequired();

        builder.Property(decision => decision.Reason)
            .HasColumnName("reason");

        builder.Property(decision => decision.PriorStatus)
            .HasColumnName("prior_status")
            .IsRequired();

        builder.Property(decision => decision.PesoVersionAtDecision)
            .HasColumnName("pesos_version_at_decision")
            .IsRequired();

        builder.Property(decision => decision.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.HasOne<PesoEntity>()
            .WithMany()
            .HasForeignKey(decision => decision.PesoId)
            .HasConstraintName(PesoForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(decision => decision.DecidedByUserId)
            .HasConstraintName(DecidedByForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(decision => decision.PesoId)
            .HasDatabaseName(PesoIndexName);

        builder.HasIndex(decision => decision.DecidedAt)
            .IsDescending()
            .HasDatabaseName(DecidedAtIndexName);
    }
}

/// <summary>
/// Additive P2-T06 index contribution to the CLOSED <c>pesos</c> entity model: the single deferred
/// pending-list query index <c>IX_pesos_reviewable (status, submitted_at DESC)</c> that P2-T05
/// §17.5 explicitly reserved for this slice (contract §7.3/§25).
/// </summary>
/// <remarks>
/// This is a NEW file — the closed P2-T05 <c>PesoEntityConfiguration</c> is never edited (App. A).
/// EF merges multiple <c>IEntityTypeConfiguration&lt;PesoEntity&gt;</c> implementations; this one
/// contributes ONLY the index — an index-only additive statement, no column change to
/// <c>pesos</c>.</remarks>
public sealed class PesoReviewableIndexConfiguration : IEntityTypeConfiguration<PesoEntity>
{
    /// <summary>Database name of the deferred pending-list index.</summary>
    public const string ReviewableIndexName = "IX_pesos_reviewable";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PesoEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasIndex(peso => new { peso.Status, peso.SubmittedAt })
            .IsDescending(false, true)
            .HasDatabaseName(ReviewableIndexName);
    }
}