using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentsService.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class DropDocumentText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_text",
                columns: table => new
                {
                    document_id = table.Column<string>(type: "text", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    extracted_at = table.Column<string>(type: "text", nullable: true),
                    page_count = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    text = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_text", x => x.document_id);
                    table.ForeignKey(
                        name: "fk_document_text_document_records_document_id",
                        column: x => x.document_id,
                        principalTable: "document_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }
    }
}
