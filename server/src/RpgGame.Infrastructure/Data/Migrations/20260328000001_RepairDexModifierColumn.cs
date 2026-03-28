using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RpgGame.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RepairDexModifierColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"EncounterCombatants\" ADD COLUMN IF NOT EXISTS \"DexModifier\" integer NOT NULL DEFAULT 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"EncounterCombatants\" DROP COLUMN IF EXISTS \"DexModifier\";");
        }
    }
}
