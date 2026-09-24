using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DMO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EmailTemplateGroupRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "email_list_id",
                table: "email_templates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "machine_group",
                table: "email_templates",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_templates_email_list_id",
                table: "email_templates",
                column: "email_list_id");

            migrationBuilder.AddCheckConstraint(
                name: "email_templates_machine_group_check",
                table: "email_templates",
                sql: "machine_group IS NULL OR machine_group IN ('B','C')");

            migrationBuilder.AddForeignKey(
                name: "email_templates_email_list_id_fkey",
                table: "email_templates",
                column: "email_list_id",
                principalTable: "email_lists",
                principalColumn: "email_list_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "email_templates_email_list_id_fkey",
                table: "email_templates");

            migrationBuilder.DropIndex(
                name: "IX_email_templates_email_list_id",
                table: "email_templates");

            migrationBuilder.DropCheckConstraint(
                name: "email_templates_machine_group_check",
                table: "email_templates");

            migrationBuilder.DropColumn(
                name: "email_list_id",
                table: "email_templates");

            migrationBuilder.DropColumn(
                name: "machine_group",
                table: "email_templates");
        }
    }
}
