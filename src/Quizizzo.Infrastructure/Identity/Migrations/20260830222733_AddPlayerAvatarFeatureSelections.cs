using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quizizzo.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerAvatarFeatureSelections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CharacterBrowShape",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CharacterEyeColour",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CharacterEyeSize",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CharacterFaceShape",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CharacterHairStyle",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CharacterNoseShape",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CharacterShirtStyle",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CharacterShoeStyle",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CharacterTrouserStyle",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CharacterBrowShape",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "CharacterEyeColour",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "CharacterEyeSize",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "CharacterFaceShape",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "CharacterHairStyle",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "CharacterNoseShape",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "CharacterShirtStyle",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "CharacterShoeStyle",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "CharacterTrouserStyle",
                table: "Players");

        }
    }
}
