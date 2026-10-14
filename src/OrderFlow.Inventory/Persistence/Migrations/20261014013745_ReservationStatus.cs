using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderFlow.Inventory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReservationStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "reservations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                // Reviewed by hand: rows from before spec 004 are all holds (released rows were deleted).
                defaultValue: "Held");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "reservations");
        }
    }
}
