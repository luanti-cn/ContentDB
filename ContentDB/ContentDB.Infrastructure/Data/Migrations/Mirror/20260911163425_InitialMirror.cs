using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentDB.Infrastructure.Data.Migrations.Mirror
{
    /// <inheritdoc />
    public partial class InitialMirror : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cached_response",
                columns: table => new
                {
                    CacheKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cached_response", x => x.CacheKey);
                });

            migrationBuilder.CreateTable(
                name: "mirrored_file",
                columns: table => new
                {
                    UpstreamPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsStored = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Downloads = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mirrored_file", x => x.UpstreamPath);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cached_response_ExpiresAt",
                table: "cached_response",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_mirrored_file_IsStored",
                table: "mirrored_file",
                column: "IsStored");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cached_response");

            migrationBuilder.DropTable(
                name: "mirrored_file");
        }
    }
}
