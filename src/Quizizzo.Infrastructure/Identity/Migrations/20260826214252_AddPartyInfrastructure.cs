using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quizizzo.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddPartyInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Parties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HostUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RoomCode = table.Column<string>(type: "character(4)", fixedLength: true, maxLength: 4, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Parties_AspNetUsers_HostUserId",
                        column: x => x.HostUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DisplaySessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionTokenHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    PairingCode = table.Column<string>(type: "character(8)", fixedLength: true, maxLength: 8, nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PairingExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PairedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisplaySessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DisplaySessions_Parties_PartyId",
                        column: x => x.PartyId,
                        principalTable: "Parties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DisplaySessions_CreatedAt",
                table: "DisplaySessions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DisplaySessions_PairingCode",
                table: "DisplaySessions",
                column: "PairingCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DisplaySessions_PartyId",
                table: "DisplaySessions",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_DisplaySessions_SessionTokenHash",
                table: "DisplaySessions",
                column: "SessionTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Parties_CreatedAt",
                table: "Parties",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Parties_HostUserId",
                table: "Parties",
                column: "HostUserId",
                unique: true,
                filter: "\"Status\" IN (0, 1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_Parties_RoomCode",
                table: "Parties",
                column: "RoomCode",
                unique: true,
                filter: "\"Status\" IN (0, 1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DisplaySessions");

            migrationBuilder.DropTable(
                name: "Parties");
        }
    }
}
