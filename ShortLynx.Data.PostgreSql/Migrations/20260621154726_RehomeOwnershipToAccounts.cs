using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortLynx.Data.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class RehomeOwnershipToAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomDomains_UserAccounts_UserAccountId",
                table: "CustomDomains");

            // 1. Add the owner column to each resource as NULLABLE so existing rows survive.
            migrationBuilder.AddColumn<Guid>(name: "AccountId", table: "Links", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "AccountId", table: "CustomDomains", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "AccountId", table: "ApiKeys", type: "uuid", nullable: true);

            // CustomDomains.UserAccountId becomes audit-only (nullable).
            migrationBuilder.AlterColumn<Guid>(
                name: "UserAccountId",
                table: "CustomDomains",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // 2. Backfill: a personal account (with an Owner membership) per existing user, an account
            //    per user-less API key, then point every resource at the correct account.
            //    Requires PostgreSQL 13+ (built-in gen_random_uuid()).
            migrationBuilder.Sql("""
                ALTER TABLE "Accounts" ADD COLUMN "_BackfillUserId" uuid;
                ALTER TABLE "Accounts" ADD COLUMN "_BackfillApiKeyId" uuid;

                INSERT INTO "Accounts" ("Id", "Name", "CreatedAt", "IsActive", "_BackfillUserId")
                SELECT gen_random_uuid(), u."Email", now(), true, u."Id" FROM "UserAccounts" u;

                INSERT INTO "Memberships" ("Id", "AccountId", "UserAccountId", "Role", "CreatedAt", "InvitedByUserAccountId")
                SELECT gen_random_uuid(), a."Id", a."_BackfillUserId", 3, now(), NULL
                FROM "Accounts" a WHERE a."_BackfillUserId" IS NOT NULL;

                INSERT INTO "Accounts" ("Id", "Name", "CreatedAt", "IsActive", "_BackfillApiKeyId")
                SELECT gen_random_uuid(), k."Name", now(), true, k."Id"
                FROM "ApiKeys" k WHERE k."UserAccountId" IS NULL;

                UPDATE "ApiKeys" k SET "AccountId" = a."Id"
                FROM "Accounts" a WHERE a."_BackfillUserId" = k."UserAccountId";
                UPDATE "ApiKeys" k SET "AccountId" = a."Id"
                FROM "Accounts" a WHERE a."_BackfillApiKeyId" = k."Id" AND k."UserAccountId" IS NULL;

                UPDATE "CustomDomains" d SET "AccountId" = a."Id"
                FROM "Accounts" a WHERE a."_BackfillUserId" = d."UserAccountId";

                UPDATE "Links" l SET "AccountId" = a."Id"
                FROM "Accounts" a WHERE l."UserAccountId" IS NOT NULL AND a."_BackfillUserId" = l."UserAccountId";
                UPDATE "Links" l SET "AccountId" = k."AccountId"
                FROM "ApiKeys" k WHERE l."AccountId" IS NULL AND l."ApiKeyId" = k."Id";

                ALTER TABLE "Accounts" DROP COLUMN "_BackfillUserId";
                ALTER TABLE "Accounts" DROP COLUMN "_BackfillApiKeyId";
                """);

            // 3. Every row now has an account — enforce non-null.
            migrationBuilder.AlterColumn<Guid>(name: "AccountId", table: "Links", type: "uuid", nullable: false, oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
            migrationBuilder.AlterColumn<Guid>(name: "AccountId", table: "CustomDomains", type: "uuid", nullable: false, oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
            migrationBuilder.AlterColumn<Guid>(name: "AccountId", table: "ApiKeys", type: "uuid", nullable: false, oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Links_AccountId",
                table: "Links",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomDomains_AccountId",
                table: "CustomDomains",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_AccountId",
                table: "ApiKeys",
                column: "AccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_ApiKeys_Accounts_AccountId",
                table: "ApiKeys",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomDomains_Accounts_AccountId",
                table: "CustomDomains",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomDomains_UserAccounts_UserAccountId",
                table: "CustomDomains",
                column: "UserAccountId",
                principalTable: "UserAccounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Links_Accounts_AccountId",
                table: "Links",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApiKeys_Accounts_AccountId",
                table: "ApiKeys");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomDomains_Accounts_AccountId",
                table: "CustomDomains");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomDomains_UserAccounts_UserAccountId",
                table: "CustomDomains");

            migrationBuilder.DropForeignKey(
                name: "FK_Links_Accounts_AccountId",
                table: "Links");

            migrationBuilder.DropIndex(
                name: "IX_Links_AccountId",
                table: "Links");

            migrationBuilder.DropIndex(
                name: "IX_CustomDomains_AccountId",
                table: "CustomDomains");

            migrationBuilder.DropIndex(
                name: "IX_ApiKeys_AccountId",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "Links");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "CustomDomains");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "ApiKeys");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserAccountId",
                table: "CustomDomains",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomDomains_UserAccounts_UserAccountId",
                table: "CustomDomains",
                column: "UserAccountId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
