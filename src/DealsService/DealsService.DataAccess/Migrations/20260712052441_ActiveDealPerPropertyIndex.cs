using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DealsService.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class ActiveDealPerPropertyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_deals_property_id",
                table: "deals");

            migrationBuilder.CreateIndex(
                name: "ix_deals_property_id_active",
                table: "deals",
                column: "property_id",
                unique: true,
                filter: "stage NOT IN ('Acquired', 'Dead')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_deals_property_id_active",
                table: "deals");

            migrationBuilder.CreateIndex(
                name: "ix_deals_property_id",
                table: "deals",
                column: "property_id");
        }
    }
}
