using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Eshop.Data.Migrations
{
    /// <inheritdoc />
    public partial class Handovers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "handovers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    contract_id = table.Column<int>(type: "integer", nullable: false),
                    stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    stage_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    stage_changed_by = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    down_payment_received = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    down_payment_received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    down_payment_received_by = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    handed_over_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_handovers", x => x.id);
                    table.ForeignKey(
                        name: "fk_handovers_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_handovers_contract_id",
                table: "handovers",
                column: "contract_id",
                unique: true);

            // Contracts accepted before Handovers existed start at Preparing.
            migrationBuilder.Sql("""
                INSERT INTO handovers (contract_id, stage, stage_changed_at)
                SELECT id, 'Preparing', accepted_at FROM contracts
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "handovers");
        }
    }
}
