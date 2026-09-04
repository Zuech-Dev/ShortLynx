using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortLynx.Data.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddCityClickAggregates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnableCityAggregates",
                table: "Accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CityClickDaily",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkId = table.Column<Guid>(type: "uuid", nullable: false),
                    City = table.Column<string>(type: "text", nullable: false),
                    Country = table.Column<string>(type: "text", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Count = table.Column<long>(type: "bigint", nullable: false),
                    UniqueCount = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CityClickDaily", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CityClickDaily_Links_LinkId",
                        column: x => x.LinkId,
                        principalTable: "Links",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CityClickDailyVisitors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkId = table.Column<Guid>(type: "uuid", nullable: false),
                    City = table.Column<string>(type: "text", nullable: false),
                    Country = table.Column<string>(type: "text", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    HashedIp = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CityClickDailyVisitors", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CityClickDaily_LinkId_City_Country_Date",
                table: "CityClickDaily",
                columns: new[] { "LinkId", "City", "Country", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CityClickDailyVisitors_LinkId_City_Country_Date_HashedIp",
                table: "CityClickDailyVisitors",
                columns: new[] { "LinkId", "City", "Country", "Date", "HashedIp" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CityClickDaily");

            migrationBuilder.DropTable(
                name: "CityClickDailyVisitors");

            migrationBuilder.DropColumn(
                name: "EnableCityAggregates",
                table: "Accounts");
        }
    }
}
