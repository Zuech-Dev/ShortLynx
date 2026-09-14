using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortLynx.Data.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddStateToGeoCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CityClickDailyVisitors_LinkId_City_Country_Date_HashedIp",
                table: "CityClickDailyVisitors");

            migrationBuilder.DropIndex(
                name: "IX_CityClickDaily_LinkId_City_Country_Date",
                table: "CityClickDaily");

            migrationBuilder.AlterColumn<string>(
                name: "City",
                table: "CityClickDailyVisitors",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "CityClickDailyVisitors",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "City",
                table: "CityClickDaily",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "CityClickDaily",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CityClickDailyVisitors_LinkId_City_State_Country_Date_Hashe~",
                table: "CityClickDailyVisitors",
                columns: new[] { "LinkId", "City", "State", "Country", "Date", "HashedIp" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CityClickDaily_LinkId_City_State_Country_Date",
                table: "CityClickDaily",
                columns: new[] { "LinkId", "City", "State", "Country", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CityClickDailyVisitors_LinkId_City_State_Country_Date_Hashe~",
                table: "CityClickDailyVisitors");

            migrationBuilder.DropIndex(
                name: "IX_CityClickDaily_LinkId_City_State_Country_Date",
                table: "CityClickDaily");

            migrationBuilder.DropColumn(
                name: "State",
                table: "CityClickDailyVisitors");

            migrationBuilder.DropColumn(
                name: "State",
                table: "CityClickDaily");

            migrationBuilder.AlterColumn<string>(
                name: "City",
                table: "CityClickDailyVisitors",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "City",
                table: "CityClickDaily",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CityClickDailyVisitors_LinkId_City_Country_Date_HashedIp",
                table: "CityClickDailyVisitors",
                columns: new[] { "LinkId", "City", "Country", "Date", "HashedIp" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CityClickDaily_LinkId_City_Country_Date",
                table: "CityClickDaily",
                columns: new[] { "LinkId", "City", "Country", "Date" },
                unique: true);
        }
    }
}
