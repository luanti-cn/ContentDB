using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ContentDB.Infrastructure.Data.Migrations.App
{
    /// <inheritdoc />
    public partial class AddMultiplayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "game_server",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Address = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    WebsiteUrl = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    OwnerId = table.Column<int>(type: "integer", nullable: false),
                    ReportTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReportTokenPrefix = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    PlayersOnline = table.Column<int>(type: "integer", nullable: false),
                    PlayersMax = table.Column<int>(type: "integer", nullable: false),
                    Motd = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Listed = table.Column<bool>(type: "boolean", nullable: false),
                    Verified = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_server", x => x.Id);
                    table.ForeignKey(
                        name: "FK_game_server_user_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "party",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    LeaderId = table.Column<int>(type: "integer", nullable: false),
                    ServerAddress = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_party", x => x.Id);
                    table.ForeignKey(
                        name: "FK_party_user_LeaderId",
                        column: x => x.LeaderId,
                        principalTable: "user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "party_member",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PartyId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_party_member", x => x.Id);
                    table.ForeignKey(
                        name: "FK_party_member_party_PartyId",
                        column: x => x.PartyId,
                        principalTable: "party",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_party_member_user_UserId",
                        column: x => x.UserId,
                        principalTable: "user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_game_server_Address",
                table: "game_server",
                column: "Address",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_game_server_OwnerId",
                table: "game_server",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_party_Code",
                table: "party",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_party_LeaderId",
                table: "party",
                column: "LeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_party_member_PartyId_UserId",
                table: "party_member",
                columns: new[] { "PartyId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_party_member_UserId",
                table: "party_member",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_server");

            migrationBuilder.DropTable(
                name: "party_member");

            migrationBuilder.DropTable(
                name: "party");
        }
    }
}
