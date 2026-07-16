using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentsService.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_records",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    deal_id = table.Column<string>(type: "text", nullable: true),
                    document_type = table.Column<string>(type: "text", nullable: true),
                    uploaded_by_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<string>(type: "text", nullable: false),
                    confirmed_at = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "document_text",
                columns: table => new
                {
                    document_id = table.Column<string>(type: "text", nullable: false),
                    text = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    extracted_at = table.Column<string>(type: "text", nullable: true),
                    page_count = table.Column<int>(type: "integer", nullable: true)
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

            migrationBuilder.CreateIndex(
                name: "ix_document_records_deal_id",
                table: "document_records",
                column: "deal_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_records_status",
                table: "document_records",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_document_records_storage_key",
                table: "document_records",
                column: "storage_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_text");

            migrationBuilder.DropTable(
                name: "document_records");
        }
    }
}
