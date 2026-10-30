using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Eshop.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuctionClose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "forfeited_at",
                table: "lots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "paid_at",
                table: "lots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "payment_branch_id",
                table: "lots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_recorded_by",
                table: "lots",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reoffered_as_lot_id",
                table: "lots",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_lots_payment_branch_id",
                table: "lots",
                column: "payment_branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_lots_reoffered_as_lot_id",
                table: "lots",
                column: "reoffered_as_lot_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_lots_branches_payment_branch_id",
                table: "lots",
                column: "payment_branch_id",
                principalTable: "branches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_lots_lots_reoffered_as_lot_id",
                table: "lots",
                column: "reoffered_as_lot_id",
                principalTable: "lots",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_lots_branches_payment_branch_id",
                table: "lots");

            migrationBuilder.DropForeignKey(
                name: "fk_lots_lots_reoffered_as_lot_id",
                table: "lots");

            migrationBuilder.DropIndex(
                name: "ix_lots_payment_branch_id",
                table: "lots");

            migrationBuilder.DropIndex(
                name: "ix_lots_reoffered_as_lot_id",
                table: "lots");

            migrationBuilder.DropColumn(
                name: "forfeited_at",
                table: "lots");

            migrationBuilder.DropColumn(
                name: "paid_at",
                table: "lots");

            migrationBuilder.DropColumn(
                name: "payment_branch_id",
                table: "lots");

            migrationBuilder.DropColumn(
                name: "payment_recorded_by",
                table: "lots");

            migrationBuilder.DropColumn(
                name: "reoffered_as_lot_id",
                table: "lots");
        }
    }
}
