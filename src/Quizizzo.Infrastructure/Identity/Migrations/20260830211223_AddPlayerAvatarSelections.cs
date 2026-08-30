using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quizizzo.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerAvatarSelections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CharacterHairColour",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CharacterPresentation",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CharacterShirtColour",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CharacterShoeColour",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CharacterSkinTone",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CharacterTrouserColour",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CharacterTrouserLength",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CharacterHairColour",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "CharacterPresentation",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "CharacterShirtColour",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "CharacterShoeColour",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "CharacterSkinTone",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "CharacterTrouserColour",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "CharacterTrouserLength",
                table: "Players");
        }
    }
}
