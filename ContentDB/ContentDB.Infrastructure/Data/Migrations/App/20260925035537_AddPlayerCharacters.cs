using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ContentDB.Infrastructure.Data.Migrations.App
{
    /// <inheritdoc />
    public partial class AddPlayerCharacters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CharacterId",
                table: "server_credential",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "player_character",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PasswordType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FixedPasswordEncrypted = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PasswordListEncrypted = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    RotateIndex = table.Column<int>(type: "integer", nullable: false),
                    SkinUrl = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_character", x => x.Id);
                    table.ForeignKey(
                        name: "FK_player_character_user_UserId",
                        column: x => x.UserId,
                        principalTable: "user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_server_credential_CharacterId",
                table: "server_credential",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_player_character_UserId_Name",
                table: "player_character",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_server_credential_player_character_CharacterId",
                table: "server_credential",
                column: "CharacterId",
                principalTable: "player_character",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_server_credential_player_character_CharacterId",
                table: "server_credential");

            migrationBuilder.DropTable(
                name: "player_character");

            migrationBuilder.DropIndex(
                name: "IX_server_credential_CharacterId",
                table: "server_credential");

            migrationBuilder.DropColumn(
                name: "CharacterId",
                table: "server_credential");
        }
    }
}
