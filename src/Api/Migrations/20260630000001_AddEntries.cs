using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BillsBackend.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bill_entry",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    owner_id = table.Column<long>(type: "bigint", nullable: false),
                    bill_id = table.Column<long>(type: "bigint", nullable: false),
                    ref_year = table.Column<int>(type: "integer", nullable: false),
                    ref_month = table.Column<int>(type: "integer", nullable: false),
                    planned_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    actual_amount = table.Column<decimal>(type: "numeric", nullable: true),
                    split_ratio_snapshot = table.Column<decimal>(type: "numeric", nullable: false),
                    person_id = table.Column<long>(type: "bigint", nullable: true),
                    paid = table.Column<bool>(type: "boolean", nullable: false),
                    paid_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received = table.Column<bool>(type: "boolean", nullable: false),
                    received_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bill_entry", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "income_entry",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    owner_id = table.Column<long>(type: "bigint", nullable: false),
                    income_id = table.Column<long>(type: "bigint", nullable: false),
                    ref_year = table.Column<int>(type: "integer", nullable: false),
                    ref_month = table.Column<int>(type: "integer", nullable: false),
                    planned_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    actual_amount = table.Column<decimal>(type: "numeric", nullable: true),
                    received = table.Column<bool>(type: "boolean", nullable: false),
                    received_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_income_entry", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bill_entry_bill_id_ref_year_ref_month",
                table: "bill_entry",
                columns: new[] { "bill_id", "ref_year", "ref_month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_income_entry_income_id_ref_year_ref_month",
                table: "income_entry",
                columns: new[] { "income_id", "ref_year", "ref_month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bill_entry");

            migrationBuilder.DropTable(
                name: "income_entry");
        }
    }
}
