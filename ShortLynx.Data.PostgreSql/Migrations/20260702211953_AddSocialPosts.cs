using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortLynx.Data.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddSocialPosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SocialPosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkId = table.Column<Guid>(type: "uuid", nullable: false),
                    SocialConnectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Platform = table.Column<int>(type: "integer", nullable: false),
                    Handle = table.Column<string>(type: "text", nullable: false),
                    ExternalPostId = table.Column<string>(type: "text", nullable: false),
                    PostUrl = table.Column<string>(type: "text", nullable: true),
                    Text = table.Column<string>(type: "text", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Impressions = table.Column<long>(type: "bigint", nullable: true),
                    Likes = table.Column<long>(type: "bigint", nullable: true),
                    Reposts = table.Column<long>(type: "bigint", nullable: true),
                    Replies = table.Column<long>(type: "bigint", nullable: true),
                    MetricsUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialPosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocialPosts_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SocialPosts_Links_LinkId",
                        column: x => x.LinkId,
                        principalTable: "Links",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SocialPosts_SocialConnections_SocialConnectionId",
                        column: x => x.SocialConnectionId,
                        principalTable: "SocialConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SocialPosts_AccountId",
                table: "SocialPosts",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPosts_LinkId",
                table: "SocialPosts",
                column: "LinkId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPosts_SocialConnectionId",
                table: "SocialPosts",
                column: "SocialConnectionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SocialPosts");
        }
    }
}
