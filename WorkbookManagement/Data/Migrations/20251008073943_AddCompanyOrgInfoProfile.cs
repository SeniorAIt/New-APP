using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkbookManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyOrgInfoProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrgInfoJson",
                table: "Companies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OrgInfoUpdatedAtUtc",
                table: "Companies",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrgInfoJson",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "OrgInfoUpdatedAtUtc",
                table: "Companies");
        }
    }
}
