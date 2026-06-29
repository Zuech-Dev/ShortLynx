using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortLynx.Data.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkUserOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Links_ApiKeys_ApiKeyId",
                table: "Links");

            migrationBuilder.AlterColumn<Guid>(
                name: "ApiKeyId",
                table: "Links",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "UserAccountId",
                table: "Links",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Links_UserAccountId",
                table: "Links",
                column: "UserAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_Links_ApiKeys_ApiKeyId",
                table: "Links",
                column: "ApiKeyId",
                principalTable: "ApiKeys",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Links_UserAccounts_UserAccountId",
                table: "Links",
                column: "UserAccountId",
                principalTable: "UserAccounts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Links_ApiKeys_ApiKeyId",
                table: "Links");

            migrationBuilder.DropForeignKey(
                name: "FK_Links_UserAccounts_UserAccountId",
                table: "Links");

            migrationBuilder.DropIndex(
                name: "IX_Links_UserAccountId",
                table: "Links");

            migrationBuilder.DropColumn(
                name: "UserAccountId",
                table: "Links");

            migrationBuilder.AlterColumn<Guid>(
                name: "ApiKeyId",
                table: "Links",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Links_ApiKeys_ApiKeyId",
                table: "Links",
                column: "ApiKeyId",
                principalTable: "ApiKeys",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
