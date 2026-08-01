using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DealsService.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class DealPropertyMetricsSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "market_cap_rate_benchmark",
                table: "deals",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "occupancy_rate",
                table: "deals",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "market_cap_rate_benchmark",
                table: "deals");

            migrationBuilder.DropColumn(
                name: "occupancy_rate",
                table: "deals");
        }
    }
}
