using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ContentDB.Infrastructure.Data.Migrations.App
{
    /// <inheritdoc />
    public partial class AddFriendsPresence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_paired_device_UserId",
                table: "paired_device");

            migrationBuilder.AddColumn<string>(
                name: "CurrentServerAddress",
                table: "paired_device",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PresenceUpdatedAt",
                table: "paired_device",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "friend_link",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequesterId = table.Column<int>(type: "integer", nullable: false),
                    AddresseeId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RespondedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_friend_link", x => x.Id);
                    table.ForeignKey(
                        name: "FK_friend_link_user_AddresseeId",
                        column: x => x.AddresseeId,
                        principalTable: "user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_friend_link_user_RequesterId",
                        column: x => x.RequesterId,
                        principalTable: "user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_paired_device_UserId_PresenceUpdatedAt",
                table: "paired_device",
                columns: new[] { "UserId", "PresenceUpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_friend_link_AddresseeId",
                table: "friend_link",
                column: "AddresseeId");

            migrationBuilder.CreateIndex(
                name: "IX_friend_link_RequesterId_AddresseeId",
                table: "friend_link",
                columns: new[] { "RequesterId", "AddresseeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "friend_link");

            migrationBuilder.DropIndex(
                name: "IX_paired_device_UserId_PresenceUpdatedAt",
                table: "paired_device");

            migrationBuilder.DropColumn(
                name: "CurrentServerAddress",
                table: "paired_device");

            migrationBuilder.DropColumn(
                name: "PresenceUpdatedAt",
                table: "paired_device");

            migrationBuilder.CreateIndex(
                name: "IX_paired_device_UserId",
                table: "paired_device",
                column: "UserId");
        }
    }
}
