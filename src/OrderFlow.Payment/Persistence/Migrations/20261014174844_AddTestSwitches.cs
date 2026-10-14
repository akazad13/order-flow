using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderFlow.Payment.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTestSwitches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "test_switches",
                columns: table => new
                {
                    CustomerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DeclineReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Stalled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_test_switches", x => x.CustomerId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "test_switches");
        }
    }
}
