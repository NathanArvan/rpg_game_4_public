using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RpgGame.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCombatProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DamageDie",
                table: "EncounterCombatants");

            migrationBuilder.DropColumn(
                name: "MaxHp",
                table: "EncounterCombatants");

            migrationBuilder.DropColumn(
                name: "Speed",
                table: "EncounterCombatants");

            migrationBuilder.AddColumn<string>(
                name: "Profile",
                table: "EncounterCombatants",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CombatProfile",
                table: "Characters",
                type: "text",
                nullable: false,
                defaultValue: "skirmisher");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Profile",
                table: "EncounterCombatants");

            migrationBuilder.DropColumn(
                name: "CombatProfile",
                table: "Characters");

            migrationBuilder.AddColumn<string>(
                name: "DamageDie",
                table: "EncounterCombatants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxHp",
                table: "EncounterCombatants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Speed",
                table: "EncounterCombatants",
                type: "integer",
                nullable: true);
        }
    }
}
