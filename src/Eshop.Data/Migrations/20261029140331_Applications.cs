using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Eshop.Data.Migrations
{
    /// <inheritdoc />
    public partial class Applications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "date_of_birth",
                table: "customers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "full_name",
                table: "customers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "id_number",
                table: "customers",
                type: "character varying(13)",
                maxLength: 13,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "id_verified_at",
                table: "customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "applications",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    flat_rate = table.Column<decimal>(type: "numeric(6,4)", precision: 6, scale: 4, nullable: false),
                    term = table.Column<int>(type: "integer", nullable: false),
                    down_payment = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    installment = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    total_payable = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    handover = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    delivery_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    delivery_province = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    branch_id = table.Column<int>(type: "integer", nullable: true),
                    id_number = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    id_card_image_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    id_card_content_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    credit_limit = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_applications", x => x.id);
                    table.ForeignKey(
                        name: "fk_applications_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_applications_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_applications_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_applications_branch_id",
                table: "applications",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_applications_customer_id",
                table: "applications",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_applications_id_number",
                table: "applications",
                column: "id_number");

            migrationBuilder.CreateIndex(
                name: "ix_applications_product_id",
                table: "applications",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_applications_status",
                table: "applications",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "applications");

            migrationBuilder.DropColumn(
                name: "date_of_birth",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "full_name",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "id_number",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "id_verified_at",
                table: "customers");
        }
    }
}
