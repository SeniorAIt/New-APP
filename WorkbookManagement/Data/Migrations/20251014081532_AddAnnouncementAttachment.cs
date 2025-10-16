using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkbookManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnouncementAttachment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttachmentContentType",
                table: "Announcements",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentFileName",
                table: "Announcements",
                type: "TEXT",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentPath",
                table: "Announcements",
                type: "TEXT",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AttachmentSizeBytes",
                table: "Announcements",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AttachmentUploadedAtUtc",
                table: "Announcements",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttachmentContentType",
                table: "Announcements");

            migrationBuilder.DropColumn(
                name: "AttachmentFileName",
                table: "Announcements");

            migrationBuilder.DropColumn(
                name: "AttachmentPath",
                table: "Announcements");

            migrationBuilder.DropColumn(
                name: "AttachmentSizeBytes",
                table: "Announcements");

            migrationBuilder.DropColumn(
                name: "AttachmentUploadedAtUtc",
                table: "Announcements");
        }
    }
}
