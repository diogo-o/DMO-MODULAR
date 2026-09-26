using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DMO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ControloComparacaoDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "comparacoes",
                columns: table => new
                {
                    comparacao_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    peso_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comparacoes", x => x.comparacao_id);
                    table.CheckConstraint("comparacoes_confirmed_check", "(confirmed_at IS NULL AND confirmed_by_user_id IS NULL) OR (confirmed_at IS NOT NULL AND confirmed_by_user_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_comparacoes_pesos_peso_id",
                        column: x => x.peso_id,
                        principalTable: "pesos",
                        principalColumn: "peso_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_comparacoes_users_confirmed_by_user_id",
                        column: x => x.confirmed_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_comparacoes_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "comparacao_cm_subjects",
                columns: table => new
                {
                    comparacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    decision = table.Column<string>(type: "text", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comparacao_cm_subjects", x => new { x.comparacao_id, x.cm_id });
                    table.CheckConstraint("comparacao_cm_subjects_decision_check", "decision IS NULL OR decision IN ('manter','colocar_de_parte')");
                    table.CheckConstraint("comparacao_cm_subjects_decision_state_check", "(decision IS NULL AND decided_by_user_id IS NULL AND decided_at IS NULL) OR (decision IS NOT NULL AND decided_by_user_id IS NOT NULL AND decided_at IS NOT NULL)");
                    table.CheckConstraint("comparacao_cm_subjects_reason_check", "decision IS NULL OR decision <> 'colocar_de_parte' OR (reason IS NOT NULL AND btrim(reason) <> '')");
                    table.ForeignKey(
                        name: "FK_comparacao_cm_subjects_cm_contexts_cm_id",
                        column: x => x.cm_id,
                        principalTable: "cm_contexts",
                        principalColumn: "cm_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_comparacao_cm_subjects_comparacoes_comparacao_id",
                        column: x => x.comparacao_id,
                        principalTable: "comparacoes",
                        principalColumn: "comparacao_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_comparacao_cm_subjects_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_comparacao_cm_subjects_users_decided_by_user_id",
                        column: x => x.decided_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "comparacao_measurement_rows",
                columns: table => new
                {
                    comparacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_position = table.Column<int>(type: "integer", nullable: false),
                    water_weight_g = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    capacity_cm3 = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    glass_weight_g = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comparacao_measurement_rows", x => new { x.comparacao_id, x.cm_id, x.row_position });
                    table.CheckConstraint("comparacao_measurement_rows_capacity_check", "capacity_cm3 > 0");
                    table.CheckConstraint("comparacao_measurement_rows_glass_check", "glass_weight_g > 0");
                    table.CheckConstraint("comparacao_measurement_rows_position_check", "row_position >= 1");
                    table.CheckConstraint("comparacao_measurement_rows_weight_check", "water_weight_g > 0");
                    table.ForeignKey(
                        name: "FK_comparacao_measurement_rows_comparacao_cm_subjects_comparacao_id_cm_id",
                        columns: x => new { x.comparacao_id, x.cm_id },
                        principalTable: "comparacao_cm_subjects",
                        principalColumns: new[] { "comparacao_id", "cm_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_comparacao_cm_subjects_cm_id",
                table: "comparacao_cm_subjects",
                column: "cm_id");

            migrationBuilder.CreateIndex(
                name: "IX_comparacao_cm_subjects_created_by_user_id",
                table: "comparacao_cm_subjects",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_comparacao_cm_subjects_decided_by_user_id",
                table: "comparacao_cm_subjects",
                column: "decided_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_comparacoes_confirmed_by_user_id",
                table: "comparacoes",
                column: "confirmed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_comparacoes_created_by_user_id",
                table: "comparacoes",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_comparacoes_peso_id",
                table: "comparacoes",
                column: "peso_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "comparacao_measurement_rows");

            migrationBuilder.DropTable(
                name: "comparacao_cm_subjects");

            migrationBuilder.DropTable(
                name: "comparacoes");
        }
    }
}
