using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillsBackend.Api.Migrations
{
    /// <inheritdoc />
    public partial class Updatepersonaccesslink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "token_hash",
                table: "person_access_link");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "issued_at",
                table: "person_access_link",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "token_id",
                table: "person_access_link",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_person_access_link_token_id",
                table: "person_access_link",
                column: "token_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_person_access_link_token_id",
                table: "person_access_link");

            migrationBuilder.DropColumn(
                name: "issued_at",
                table: "person_access_link");

            migrationBuilder.DropColumn(
                name: "token_id",
                table: "person_access_link");

            migrationBuilder.AddColumn<string>(
                name: "token_hash",
                table: "person_access_link",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
