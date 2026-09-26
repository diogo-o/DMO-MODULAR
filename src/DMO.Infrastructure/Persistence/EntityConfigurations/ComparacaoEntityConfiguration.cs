using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>comparacoes</c> table: one Peso Comparação EVENT header tied to
/// exactly one <c>peso_id</c>.
/// </summary>
/// <remarks>
/// Maps the <c>peso_id → comparacao_id</c> identity (a NEW comparison always allocates a NEW
/// comparacao_id — a Peso may have several comparison events over time, so <c>peso_id</c> is a
/// plain RESTRICT FK, deliberately NOT unique), the confirmation-stamp CHECK, the three RESTRICT
/// foreign keys (<c>pesos</c> + the two actor <c>users</c>) and the <c>version</c> concurrency
/// token. No column of <c>pesos</c> is touched by this slice.</remarks>
public sealed class ComparacaoEntityConfiguration : IEntityTypeConfiguration<ComparacaoEntity>
{
    /// <summary>Database name of the confirmation-stamp CHECK.</summary>
    public const string ConfirmedCheckConstraintName = "comparacoes_confirmed_check";

    /// <summary>Database name of the Peso foreign key.</summary>
    public const string PesoForeignKeyConstraintName = "FK_comparacoes_pesos_peso_id";

    /// <summary>Database name of the creation-actor foreign key.</summary>
    public const string CreatedByForeignKeyConstraintName = "FK_comparacoes_users_created_by_user_id";

    /// <summary>Database name of the confirmation-actor foreign key.</summary>
    public const string ConfirmedByForeignKeyConstraintName = "FK_comparacoes_users_confirmed_by_user_id";

    /// <summary>Database name of the FK-supporting Peso index.</summary>
    public const string PesoIndexName = "IX_comparacoes_peso_id";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ComparacaoEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("comparacoes", table =>
        {
            table.HasCheckConstraint(
                ConfirmedCheckConstraintName,
                "(confirmed_at IS NULL AND confirmed_by_user_id IS NULL) OR " +
                "(confirmed_at IS NOT NULL AND confirmed_by_user_id IS NOT NULL)");
        });

        builder.HasKey(comparacao => comparacao.ComparacaoId);
        builder.Property(comparacao => comparacao.ComparacaoId)
            .HasColumnName("comparacao_id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(comparacao => comparacao.PesoId)
            .HasColumnName("peso_id")
            .IsRequired();

        builder.Property(comparacao => comparacao.CreatedByUserId)
            .HasColumnName("created_by_user_id")
            .IsRequired();

        builder.Property(comparacao => comparacao.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Property(comparacao => comparacao.ConfirmedAt)
            .HasColumnName("confirmed_at");

        builder.Property(comparacao => comparacao.ConfirmedByUserId)
            .HasColumnName("confirmed_by_user_id");

        builder.Property(comparacao => comparacao.Version)
            .HasColumnName("version")
            .IsRequired()
            .HasDefaultValue(1)
            .IsConcurrencyToken();

        builder.Property(comparacao => comparacao.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        builder.HasOne<PesoEntity>()
            .WithMany()
            .HasForeignKey(comparacao => comparacao.PesoId)
            .HasConstraintName(PesoForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(comparacao => comparacao.CreatedByUserId)
            .HasConstraintName(CreatedByForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(comparacao => comparacao.ConfirmedByUserId)
            .HasConstraintName(ConfirmedByForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(comparacao => comparacao.PesoId)
            .HasDatabaseName(PesoIndexName);
    }
}