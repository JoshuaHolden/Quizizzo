using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quizizzo.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddPartyGameWins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerGameWins",
                columns: table => new
                {
                    GameInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WonAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerGameWins", x => new { x.PlayerId, x.GameInstanceId });
                    table.ForeignKey(
                        name: "FK_PlayerGameWins_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerGameWins_GameKey",
                table: "PlayerGameWins",
                column: "GameKey");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerGameWins_WonAt",
                table: "PlayerGameWins",
                column: "WonAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerGameWins");
        }
    }
}
