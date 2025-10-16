using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkbookManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddSubmissionBundle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DecidedAtUtc",
                table: "Submissions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecidedByUserId",
                table: "Submissions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionNote",
                table: "Submissions",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_DecidedByUserId",
                table: "Submissions",
                column: "DecidedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Submissions_AspNetUsers_DecidedByUserId",
                table: "Submissions",
                column: "DecidedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Submissions_AspNetUsers_DecidedByUserId",
                table: "Submissions");

            migrationBuilder.DropIndex(
                name: "IX_Submissions_DecidedByUserId",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "DecidedAtUtc",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "DecidedByUserId",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "DecisionNote",
                table: "Submissions");
        }
    }
}
