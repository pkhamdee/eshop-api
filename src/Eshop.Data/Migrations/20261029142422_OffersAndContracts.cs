using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Eshop.Data.Migrations
{
    /// <inheritdoc />
    public partial class OffersAndContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contract_number_counters",
                columns: table => new
                {
                    month = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    last_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_number_counters", x => x.month);
                });

            migrationBuilder.CreateTable(
                name: "eligibility_refusals",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    submission_id = table.Column<int>(type: "integer", nullable: false),
                    id_number = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    refused_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eligibility_refusals", x => x.id);
                    table.ForeignKey(
                        name: "fk_eligibility_refusals_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "offers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    application_id = table.Column<int>(type: "integer", nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offers", x => x.id);
                    table.ForeignKey(
                        name: "fk_offers_applications_application_id",
                        column: x => x.application_id,
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_offers_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contracts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    number = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    offer_id = table.Column<int>(type: "integer", nullable: false),
                    application_id = table.Column<int>(type: "integer", nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    flat_rate = table.Column<decimal>(type: "numeric(6,4)", precision: 6, scale: 4, nullable: false),
                    down_payment = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    term = table.Column<int>(type: "integer", nullable: false),
                    installment = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    total_payable = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contracts", x => x.id);
                    table.ForeignKey(
                        name: "fk_contracts_applications_application_id",
                        column: x => x.application_id,
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contracts_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contracts_offers_offer_id",
                        column: x => x.offer_id,
                        principalTable: "offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contracts_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_outbox",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    contract_id = table.Column<int>(type: "integer", nullable: false),
                    contract_number = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    enqueued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    pushed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_outbox", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_outbox_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contract_outbox_contract_id",
                table: "contract_outbox",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_outbox_contract_number",
                table: "contract_outbox",
                column: "contract_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contract_outbox_pending",
                table: "contract_outbox",
                column: "enqueued_at",
                filter: "pushed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_contracts_application_id",
                table: "contracts",
                column: "application_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contracts_customer_id_active",
                table: "contracts",
                column: "customer_id",
                unique: true,
                filter: "status IN ('Active', 'Overdue')");

            migrationBuilder.CreateIndex(
                name: "ix_contracts_number",
                table: "contracts",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contracts_offer_id",
                table: "contracts",
                column: "offer_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contracts_product_id",
                table: "contracts",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_eligibility_refusals_customer_id_submission_id",
                table: "eligibility_refusals",
                columns: new[] { "customer_id", "submission_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_offers_application_id",
                table: "offers",
                column: "application_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_offers_customer_id",
                table: "offers",
                column: "customer_id");

            // Applications Approved before Offers existed get their Offer (7 days from the Credit Decision).
            migrationBuilder.Sql("""
                INSERT INTO offers (application_id, customer_id, created_at, expires_at, status)
                SELECT id, customer_id, decided_at, decided_at + interval '7 days', 'Open'
                FROM applications WHERE status = 'Approved' AND decided_at IS NOT NULL;
                """);

            // Under-20 submissions were stored as NotEligible Applications; failing Eligibility creates no
            // Application, so they become eligibility refusals.
            migrationBuilder.Sql("""
                INSERT INTO eligibility_refusals (customer_id, submission_id, id_number, reason, refused_at)
                SELECT customer_id, id, id_number, 'Under20', COALESCE(decided_at, submitted_at)
                FROM applications WHERE status = 'NotEligible';
                DELETE FROM applications WHERE status = 'NotEligible';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contract_number_counters");

            migrationBuilder.DropTable(
                name: "contract_outbox");

            migrationBuilder.DropTable(
                name: "eligibility_refusals");

            migrationBuilder.DropTable(
                name: "contracts");

            migrationBuilder.DropTable(
                name: "offers");
        }
    }
}
