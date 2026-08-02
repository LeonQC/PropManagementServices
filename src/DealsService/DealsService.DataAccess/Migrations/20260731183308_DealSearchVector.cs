using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace DealsService.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class DealSearchVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "deals",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('english', coalesce(name, '')), 'A') || setweight(to_tsvector('english', coalesce(property_name, '')), 'B')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_deals_search_vector",
                table: "deals",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_deals_search_vector",
                table: "deals");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "deals");
        }
    }
}
