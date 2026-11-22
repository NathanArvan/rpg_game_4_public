using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RpgGame.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCombatantSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "EncounterCombatants",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "EncounterCombatants");
        }
    }
}
