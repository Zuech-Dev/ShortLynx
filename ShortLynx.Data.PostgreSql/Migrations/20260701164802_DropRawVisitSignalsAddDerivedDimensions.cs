using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortLynx.Data.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class DropRawVisitSignalsAddDerivedDimensions : Migration
    {
        // Privacy hardening (Phase 0.5): the raw User-Agent and full Referrer are fingerprinting vectors,
        // so we DROP them (discarding any stored raw values — intentional, no back-fill) and ADD the
        // low-entropy derived dimensions the writer now populates at ingest. Deliberately drop+add rather
        // than rename, so existing raw data is destroyed rather than carried over under a new name.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "Visits", "UserVisits" })
            {
                migrationBuilder.DropColumn(name: "Referrer", table: table);
                migrationBuilder.DropColumn(name: "UserAgent", table: table);

                migrationBuilder.AddColumn<string>(name: "Browser", table: table, type: "text", nullable: true);
                migrationBuilder.AddColumn<string>(name: "Os", table: table, type: "text", nullable: true);
                migrationBuilder.AddColumn<string>(name: "ReferrerHost", table: table, type: "text", nullable: true);
                migrationBuilder.AddColumn<string>(name: "Country", table: table, type: "text", nullable: true);
                migrationBuilder.AddColumn<string>(name: "Language", table: table, type: "text", nullable: true);
                migrationBuilder.AddColumn<string>(name: "NavigationType", table: table, type: "text", nullable: true);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "Visits", "UserVisits" })
            {
                migrationBuilder.DropColumn(name: "Browser", table: table);
                migrationBuilder.DropColumn(name: "Os", table: table);
                migrationBuilder.DropColumn(name: "ReferrerHost", table: table);
                migrationBuilder.DropColumn(name: "Country", table: table);
                migrationBuilder.DropColumn(name: "Language", table: table);
                migrationBuilder.DropColumn(name: "NavigationType", table: table);

                migrationBuilder.AddColumn<string>(name: "Referrer", table: table, type: "text", nullable: true);
                migrationBuilder.AddColumn<string>(name: "UserAgent", table: table, type: "text", nullable: true);
            }
        }
    }
}
