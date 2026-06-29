using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortLynx.Data.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkCustomDomainPin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomDomainId",
                table: "Links",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Links_CustomDomainId",
                table: "Links",
                column: "CustomDomainId");

            migrationBuilder.AddForeignKey(
                name: "FK_Links_CustomDomains_CustomDomainId",
                table: "Links",
                column: "CustomDomainId",
                principalTable: "CustomDomains",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Links_CustomDomains_CustomDomainId",
                table: "Links");

            migrationBuilder.DropIndex(
                name: "IX_Links_CustomDomainId",
                table: "Links");

            migrationBuilder.DropColumn(
                name: "CustomDomainId",
                table: "Links");
        }
    }
}
