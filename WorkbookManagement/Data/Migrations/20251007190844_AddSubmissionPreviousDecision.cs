using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkbookManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubmissionPreviousDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastDecidedAtUtc",
                table: "Submissions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastDecidedByUserId",
                table: "Submissions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastDecisionNote",
                table: "Submissions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastDecisionStatus",
                table: "Submissions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_LastDecidedByUserId",
                table: "Submissions",
                column: "LastDecidedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Submissions_AspNetUsers_LastDecidedByUserId",
                table: "Submissions",
                column: "LastDecidedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Submissions_AspNetUsers_LastDecidedByUserId",
                table: "Submissions");

            migrationBuilder.DropIndex(
                name: "IX_Submissions_LastDecidedByUserId",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "LastDecidedAtUtc",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "LastDecidedByUserId",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "LastDecisionNote",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "LastDecisionStatus",
                table: "Submissions");
        }
    }
}
