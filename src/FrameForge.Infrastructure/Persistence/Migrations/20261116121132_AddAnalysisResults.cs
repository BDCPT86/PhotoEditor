using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FrameForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalysisResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalysisResults",
                columns: table => new
                {
                    PhotoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AlgorithmVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SettingsHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ValueJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisResults", x => new { x.PhotoId, x.Kind });
                    table.ForeignKey(
                        name: "FK_AnalysisResults_Photos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "Photos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalysisResults");
        }
    }
}
