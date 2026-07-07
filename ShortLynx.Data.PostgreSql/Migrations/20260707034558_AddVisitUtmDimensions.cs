using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortLynx.Data.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitUtmDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UtmCampaign",
                table: "Visits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmContent",
                table: "Visits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmMedium",
                table: "Visits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmSource",
                table: "Visits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmTerm",
                table: "Visits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmCampaign",
                table: "UserVisits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmContent",
                table: "UserVisits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmMedium",
                table: "UserVisits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmSource",
                table: "UserVisits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmTerm",
                table: "UserVisits",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UtmCampaign",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "UtmContent",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "UtmMedium",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "UtmSource",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "UtmTerm",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "UtmCampaign",
                table: "UserVisits");

            migrationBuilder.DropColumn(
                name: "UtmContent",
                table: "UserVisits");

            migrationBuilder.DropColumn(
                name: "UtmMedium",
                table: "UserVisits");

            migrationBuilder.DropColumn(
                name: "UtmSource",
                table: "UserVisits");

            migrationBuilder.DropColumn(
                name: "UtmTerm",
                table: "UserVisits");
        }
    }
}
