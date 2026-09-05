using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quizizzo.Infrastructure.Identity.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260905183000_AddVoiceChoonReplays")]
public partial class AddVoiceChoonReplays : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsRetainedForReplay",
            table: "VoiceSamples",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "VoiceChoonReplays",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ShareCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PartyId = table.Column<Guid>(type: "uuid", nullable: false),
                GameInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                HostUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                SampleAssetIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_VoiceChoonReplays", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_VoiceChoonReplays_GameInstanceId",
            table: "VoiceChoonReplays",
            column: "GameInstanceId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_VoiceChoonReplays_HostUserId",
            table: "VoiceChoonReplays",
            column: "HostUserId");
        migrationBuilder.CreateIndex(
            name: "IX_VoiceChoonReplays_ShareCode",
            table: "VoiceChoonReplays",
            column: "ShareCode",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "VoiceChoonReplays");
        migrationBuilder.DropColumn(name: "IsRetainedForReplay", table: "VoiceSamples");
    }
}
