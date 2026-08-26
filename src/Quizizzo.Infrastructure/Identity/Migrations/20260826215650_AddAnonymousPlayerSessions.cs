using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quizizzo.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddAnonymousPlayerSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CharacterBodyType = table.Column<int>(type: "integer", nullable: false),
                    CharacterPrimaryColour = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    CharacterEyes = table.Column<int>(type: "integer", nullable: false),
                    CharacterMouth = table.Column<int>(type: "integer", nullable: false),
                    CharacterAccessory = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SessionTokenHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Players_Parties_PartyId",
                        column: x => x.PartyId,
                        principalTable: "Parties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Players_JoinedAt",
                table: "Players",
                column: "JoinedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Players_LastSeenAt",
                table: "Players",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_Players_PartyId_Status",
                table: "Players",
                columns: new[] { "PartyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Players_SessionTokenHash",
                table: "Players",
                column: "SessionTokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Players");
        }
    }
}
