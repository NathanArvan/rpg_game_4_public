using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RpgGame.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SimultaneousPlanningPhase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DexModifier",
                table: "EncounterCombatants");

            migrationBuilder.DropColumn(
                name: "Initiative",
                table: "EncounterCombatants");

            migrationBuilder.DropColumn(
                name: "InitiativeRoll",
                table: "EncounterCombatants");

            migrationBuilder.AddColumn<int>(
                name: "RoundNumber",
                table: "Encounters",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RoundNumber",
                table: "Encounters");

            migrationBuilder.AddColumn<int>(
                name: "DexModifier",
                table: "EncounterCombatants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Initiative",
                table: "EncounterCombatants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "InitiativeRoll",
                table: "EncounterCombatants",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
