using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace ListingsService.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddPropertyFullTextSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address_search",
                table: "properties",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "properties",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('english', coalesce(title, '')), 'A') || setweight(to_tsvector('english', coalesce(description_text, '')), 'B') || setweight(to_tsvector('english', coalesce(address_search, '')), 'C')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_properties_search_vector",
                table: "properties",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            // Backfill the denormalized address text for rows that predate this column
            // (e.g. seeded data). The STORED generated search_vector recomputes on update,
            // so this also populates the tsvector's address (weight C) component.
            migrationBuilder.Sql(@"
                UPDATE properties p
                SET address_search = NULLIF(TRIM(concat_ws(' ', a.city, a.metro_area, a.neighborhood)), '')
                FROM addresses a
                WHERE a.property_id = p.id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_properties_search_vector",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "address_search",
                table: "properties");
        }
    }
}
