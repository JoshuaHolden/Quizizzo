using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quizizzo.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableGameRuntimeSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DrawingAssets_Id",
                table: "DrawingAssets");

            migrationBuilder.CreateTable(
                name: "GameRuntimeSnapshots",
                columns: table => new
                {
                    GameInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    IsComplete = table.Column<bool>(type: "boolean", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameRuntimeSnapshots", x => x.GameInstanceId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameRuntimeSnapshots_IsComplete_UpdatedAtUtc",
                table: "GameRuntimeSnapshots",
                columns: new[] { "IsComplete", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GameRuntimeSnapshots_PartyId",
                table: "GameRuntimeSnapshots",
                column: "PartyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameRuntimeSnapshots");

            migrationBuilder.CreateIndex(
                name: "IX_DrawingAssets_Id",
                table: "DrawingAssets",
                column: "Id",
                unique: true);
        }
    }
}
