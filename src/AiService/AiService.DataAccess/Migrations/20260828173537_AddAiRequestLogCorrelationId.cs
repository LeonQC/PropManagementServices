using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiService.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddAiRequestLogCorrelationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "correlation_id",
                table: "ai_request_log",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_request_log_correlation_id",
                table: "ai_request_log",
                column: "correlation_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ai_request_log_correlation_id",
                table: "ai_request_log");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                table: "ai_request_log");
        }
    }
}
