using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkbookManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyDocumentType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "DocumentType",
                table: "CompanyDocuments",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyDocuments_CompanyId_DocumentType_UploadedAtUtc",
                table: "CompanyDocuments",
                columns: new[] { "CompanyId", "DocumentType", "UploadedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CompanyDocuments_CompanyId_DocumentType_UploadedAtUtc",
                table: "CompanyDocuments");

            migrationBuilder.AlterColumn<string>(
                name: "DocumentType",
                table: "CompanyDocuments",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");
        }
    }
}
