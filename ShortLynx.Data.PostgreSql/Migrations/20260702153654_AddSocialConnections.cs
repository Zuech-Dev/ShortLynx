using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortLynx.Data.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddSocialConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SocialConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Platform = table.Column<int>(type: "integer", nullable: false),
                    ExternalAccountId = table.Column<string>(type: "text", nullable: false),
                    Handle = table.Column<string>(type: "text", nullable: false),
                    InstanceUrl = table.Column<string>(type: "text", nullable: true),
                    AccessTokenProtected = table.Column<string>(type: "text", nullable: false),
                    RefreshTokenProtected = table.Column<string>(type: "text", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Scopes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocialConnections_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SocialConnections_UserAccounts_UserAccountId",
                        column: x => x.UserAccountId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SocialConnections_AccountId_Platform_ExternalAccountId",
                table: "SocialConnections",
                columns: new[] { "AccountId", "Platform", "ExternalAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SocialConnections_UserAccountId",
                table: "SocialConnections",
                column: "UserAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SocialConnections");
        }
    }
}
