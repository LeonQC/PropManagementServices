using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ListingsService.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "properties",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: true),
                    property_type = table.Column<string>(type: "text", nullable: false),
                    property_subtype = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    total_sqft = table.Column<double>(type: "double precision", nullable: true),
                    leasable_sqft = table.Column<double>(type: "double precision", nullable: true),
                    year_built = table.Column<int>(type: "integer", nullable: true),
                    lot_size_acres = table.Column<double>(type: "double precision", nullable: true),
                    unit_count = table.Column<int>(type: "integer", nullable: true),
                    asking_price = table.Column<double>(type: "double precision", nullable: true),
                    cap_rate = table.Column<double>(type: "double precision", nullable: true),
                    noi = table.Column<double>(type: "double precision", nullable: true),
                    occupancy_rate = table.Column<double>(type: "double precision", nullable: true),
                    market_cap_rate_benchmark = table.Column<double>(type: "double precision", nullable: true),
                    year1_noi_estimate = table.Column<double>(type: "double precision", nullable: true),
                    description_text = table.Column<string>(type: "text", nullable: true),
                    ai_summary = table.Column<string>(type: "text", nullable: true),
                    listed_at = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_properties", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "addresses",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    property_id = table.Column<string>(type: "text", nullable: false),
                    street = table.Column<string>(type: "text", nullable: true),
                    city = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "text", nullable: true),
                    zip = table.Column<string>(type: "text", nullable: true),
                    metro_area = table.Column<string>(type: "text", nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    neighborhood = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_addresses", x => x.id);
                    table.ForeignKey(
                        name: "fk_addresses_properties_property_id",
                        column: x => x.property_id,
                        principalTable: "properties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "property_features",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    property_id = table.Column<string>(type: "text", nullable: false),
                    feature_category = table.Column<string>(type: "text", nullable: true),
                    feature_name = table.Column<string>(type: "text", nullable: true),
                    feature_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_property_features", x => x.id);
                    table.ForeignKey(
                        name: "fk_property_features_properties_property_id",
                        column: x => x.property_id,
                        principalTable: "properties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "property_media",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    property_id = table.Column<string>(type: "text", nullable: false),
                    media_type = table.Column<string>(type: "text", nullable: true),
                    url = table.Column<string>(type: "text", nullable: true),
                    caption = table.Column<string>(type: "text", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_property_media", x => x.id);
                    table.ForeignKey(
                        name: "fk_property_media_properties_property_id",
                        column: x => x.property_id,
                        principalTable: "properties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_addresses_property_id",
                table: "addresses",
                column: "property_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_property_features_property_id_feature_name",
                table: "property_features",
                columns: new[] { "property_id", "feature_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_property_media_property_id",
                table: "property_media",
                column: "property_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "addresses");

            migrationBuilder.DropTable(
                name: "property_features");

            migrationBuilder.DropTable(
                name: "property_media");

            migrationBuilder.DropTable(
                name: "properties");
        }
    }
}
