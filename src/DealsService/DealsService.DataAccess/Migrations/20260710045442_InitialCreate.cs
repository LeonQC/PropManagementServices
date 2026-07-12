using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DealsService.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "deals",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    property_id = table.Column<string>(type: "text", nullable: false),
                    property_name = table.Column<string>(type: "text", nullable: false),
                    property_type = table.Column<string>(type: "text", nullable: true),
                    metro_area = table.Column<string>(type: "text", nullable: true),
                    stage = table.Column<string>(type: "text", nullable: false),
                    priority = table.Column<string>(type: "text", nullable: false),
                    owner_id = table.Column<string>(type: "text", nullable: false),
                    dead_reason = table.Column<string>(type: "text", nullable: true),
                    offer_price = table.Column<double>(type: "double precision", nullable: true),
                    projected_cap_rate = table.Column<double>(type: "double precision", nullable: true),
                    target_irr = table.Column<double>(type: "double precision", nullable: true),
                    equity_multiple = table.Column<double>(type: "double precision", nullable: true),
                    projected_close_date = table.Column<string>(type: "text", nullable: true),
                    ai_score = table.Column<double>(type: "double precision", nullable: true),
                    ai_score_rationale = table.Column<string>(type: "text", nullable: true),
                    risk_flags = table.Column<string>(type: "jsonb", nullable: true),
                    stage_entered_at = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "deal_comments",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    deal_id = table.Column<string>(type: "text", nullable: false),
                    parent_id = table.Column<string>(type: "text", nullable: true),
                    body = table.Column<string>(type: "text", nullable: false),
                    author_id = table.Column<string>(type: "text", nullable: false),
                    is_ai_generated = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deal_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_deal_comments_deals_deal_id",
                        column: x => x.deal_id,
                        principalTable: "deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "deal_documents",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    deal_id = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    file_type = table.Column<string>(type: "text", nullable: false),
                    storage_url = table.Column<string>(type: "text", nullable: true),
                    ai_summary = table.Column<string>(type: "text", nullable: true),
                    uploaded_by_id = table.Column<string>(type: "text", nullable: false),
                    uploaded_at = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deal_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_deal_documents_deals_deal_id",
                        column: x => x.deal_id,
                        principalTable: "deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "deal_stage_history",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    deal_id = table.Column<string>(type: "text", nullable: false),
                    from_stage = table.Column<string>(type: "text", nullable: true),
                    to_stage = table.Column<string>(type: "text", nullable: false),
                    changed_by_id = table.Column<string>(type: "text", nullable: false),
                    changed_at = table.Column<string>(type: "text", nullable: false),
                    days_in_stage = table.Column<int>(type: "integer", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deal_stage_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_deal_stage_history_deals_deal_id",
                        column: x => x.deal_id,
                        principalTable: "deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "deal_tasks",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    deal_id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    stage = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    assignee_id = table.Column<string>(type: "text", nullable: true),
                    due_date = table.Column<string>(type: "text", nullable: true),
                    is_from_template = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<string>(type: "text", nullable: false),
                    completed_at = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deal_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_deal_tasks_deals_deal_id",
                        column: x => x.deal_id,
                        principalTable: "deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_deal_comments_deal_id",
                table: "deal_comments",
                column: "deal_id");

            migrationBuilder.CreateIndex(
                name: "ix_deal_documents_deal_id",
                table: "deal_documents",
                column: "deal_id");

            migrationBuilder.CreateIndex(
                name: "ix_deal_stage_history_deal_id",
                table: "deal_stage_history",
                column: "deal_id");

            migrationBuilder.CreateIndex(
                name: "ix_deal_tasks_deal_id",
                table: "deal_tasks",
                column: "deal_id");

            migrationBuilder.CreateIndex(
                name: "ix_deals_owner_id",
                table: "deals",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_deals_property_id",
                table: "deals",
                column: "property_id");

            migrationBuilder.CreateIndex(
                name: "ix_deals_stage",
                table: "deals",
                column: "stage");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deal_comments");

            migrationBuilder.DropTable(
                name: "deal_documents");

            migrationBuilder.DropTable(
                name: "deal_stage_history");

            migrationBuilder.DropTable(
                name: "deal_tasks");

            migrationBuilder.DropTable(
                name: "deals");
        }
    }
}
