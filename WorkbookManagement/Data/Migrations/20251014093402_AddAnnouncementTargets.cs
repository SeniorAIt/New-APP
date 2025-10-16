using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkbookManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnouncementTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnnouncementCompanies",
                columns: table => new
                {
                    AnnouncementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementCompanies", x => new { x.AnnouncementId, x.CompanyId });
                    table.ForeignKey(
                        name: "FK_AnnouncementCompanies_Announcements_AnnouncementId",
                        column: x => x.AnnouncementId,
                        principalTable: "Announcements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AnnouncementCompanies_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementCompanies_CompanyId",
                table: "AnnouncementCompanies",
                column: "CompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnnouncementCompanies");
        }
    }
}
