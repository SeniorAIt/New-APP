using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkbookManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddSubmissionParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "WorkbookSubmissions",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<Guid>(
                name: "SubmissionId",
                table: "WorkbookSubmissions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Submissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<string>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Submissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Submissions_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Submissions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkbookSubmissions_SubmissionId",
                table: "WorkbookSubmissions",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_CompanyId",
                table: "Submissions",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_OwnerUserId",
                table: "Submissions",
                column: "OwnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkbookSubmissions_Submissions_SubmissionId",
                table: "WorkbookSubmissions",
                column: "SubmissionId",
                principalTable: "Submissions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkbookSubmissions_Submissions_SubmissionId",
                table: "WorkbookSubmissions");

            migrationBuilder.DropTable(
                name: "Submissions");

            migrationBuilder.DropIndex(
                name: "IX_WorkbookSubmissions_SubmissionId",
                table: "WorkbookSubmissions");

            migrationBuilder.DropColumn(
                name: "SubmissionId",
                table: "WorkbookSubmissions");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "WorkbookSubmissions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER");
        }
    }
}
