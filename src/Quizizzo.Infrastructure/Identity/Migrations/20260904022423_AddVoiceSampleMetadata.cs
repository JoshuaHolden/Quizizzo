using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quizizzo.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddVoiceSampleMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VoiceSamples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromptKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoiceSamples", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoiceSamples_ExpiresAtUtc",
                table: "VoiceSamples",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceSamples_SubmissionId_GameInstanceId_PlayerId_PromptKey",
                table: "VoiceSamples",
                columns: new[] { "SubmissionId", "GameInstanceId", "PlayerId", "PromptKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VoiceSamples");
        }
    }
}
