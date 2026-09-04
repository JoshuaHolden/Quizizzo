using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quizizzo.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddDynamicVoiceChoonSongs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VoiceChoonSongs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    FileName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MidiData = table.Column<byte[]>(type: "bytea", nullable: false),
                    MinimumPlayers = table.Column<int>(type: "integer", nullable: false),
                    MaximumPlayers = table.Column<int>(type: "integer", nullable: false),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: false),
                    TrackCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoiceChoonSongs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoiceChoonSongs_CreatedAtUtc",
                table: "VoiceChoonSongs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceChoonSongs_Key",
                table: "VoiceChoonSongs",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VoiceChoonSongs");
        }
    }
}
