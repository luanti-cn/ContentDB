using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentDB.Infrastructure.Data.Migrations.App
{
    /// <inheritdoc />
    public partial class AddCredentialStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InvalidReportedAt",
                table: "server_credential",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "server_credential",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvalidReportedAt",
                table: "server_credential");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "server_credential");
        }
    }
}
