using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Quizizzo.Infrastructure.Identity;

#nullable disable

namespace Quizizzo.Infrastructure.Identity.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260830231000_AddCharacterBodySize")]
public sealed class AddCharacterBodySize : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "CharacterBodySize",
            table: "Players",
            type: "integer",
            nullable: false,
            defaultValue: 1);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CharacterBodySize",
            table: "Players");
    }
}
