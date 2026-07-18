using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortLynx.Data.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddSocialPostCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SocialPosts_ShortCodes_ShortCodeId",
                table: "SocialPosts");

            migrationBuilder.DropForeignKey(
                name: "FK_Visits_ShortCodes_ShortCodeId",
                table: "Visits");

            migrationBuilder.RenameColumn(
                name: "ShortCodeId",
                table: "SocialPosts",
                newName: "SocialPostCodeId");

            migrationBuilder.RenameIndex(
                name: "IX_SocialPosts_ShortCodeId",
                table: "SocialPosts",
                newName: "IX_SocialPosts_SocialPostCodeId");

            migrationBuilder.AlterColumn<Guid>(
                name: "ShortCodeId",
                table: "Visits",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "SocialPostCodeId",
                table: "Visits",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SocialPostCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkId = table.Column<Guid>(type: "uuid", nullable: false),
                    SocialPostId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialPostCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocialPostCodes_Links_LinkId",
                        column: x => x.LinkId,
                        principalTable: "Links",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SocialPostCodes_SocialPosts_SocialPostId",
                        column: x => x.SocialPostId,
                        principalTable: "SocialPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Visits_SocialPostCodeId",
                table: "Visits",
                column: "SocialPostCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPostCodes_Code",
                table: "SocialPostCodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SocialPostCodes_LinkId",
                table: "SocialPostCodes",
                column: "LinkId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPostCodes_SocialPostId",
                table: "SocialPostCodes",
                column: "SocialPostId");

            migrationBuilder.AddForeignKey(
                name: "FK_SocialPosts_SocialPostCodes_SocialPostCodeId",
                table: "SocialPosts",
                column: "SocialPostCodeId",
                principalTable: "SocialPostCodes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Visits_ShortCodes_ShortCodeId",
                table: "Visits",
                column: "ShortCodeId",
                principalTable: "ShortCodes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Visits_SocialPostCodes_SocialPostCodeId",
                table: "Visits",
                column: "SocialPostCodeId",
                principalTable: "SocialPostCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SocialPosts_SocialPostCodes_SocialPostCodeId",
                table: "SocialPosts");

            migrationBuilder.DropForeignKey(
                name: "FK_Visits_ShortCodes_ShortCodeId",
                table: "Visits");

            migrationBuilder.DropForeignKey(
                name: "FK_Visits_SocialPostCodes_SocialPostCodeId",
                table: "Visits");

            migrationBuilder.DropTable(
                name: "SocialPostCodes");

            migrationBuilder.DropIndex(
                name: "IX_Visits_SocialPostCodeId",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "SocialPostCodeId",
                table: "Visits");

            migrationBuilder.RenameColumn(
                name: "SocialPostCodeId",
                table: "SocialPosts",
                newName: "ShortCodeId");

            migrationBuilder.RenameIndex(
                name: "IX_SocialPosts_SocialPostCodeId",
                table: "SocialPosts",
                newName: "IX_SocialPosts_ShortCodeId");

            migrationBuilder.AlterColumn<Guid>(
                name: "ShortCodeId",
                table: "Visits",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SocialPosts_ShortCodes_ShortCodeId",
                table: "SocialPosts",
                column: "ShortCodeId",
                principalTable: "ShortCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Visits_ShortCodes_ShortCodeId",
                table: "Visits",
                column: "ShortCodeId",
                principalTable: "ShortCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
