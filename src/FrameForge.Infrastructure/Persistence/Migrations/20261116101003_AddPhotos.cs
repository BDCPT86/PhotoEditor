using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FrameForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Photos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    FolderPath = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    LastModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ImportedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    CaptureLocalTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CameraMake = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CameraModel = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LensModel = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Iso = table.Column<int>(type: "INTEGER", nullable: true),
                    ExposureTime = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    FNumber = table.Column<double>(type: "REAL", nullable: true),
                    FocalLengthMm = table.Column<double>(type: "REAL", nullable: true),
                    Orientation = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    LastError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Photos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Photos_FilePath",
                table: "Photos",
                column: "FilePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Photos_FolderPath",
                table: "Photos",
                column: "FolderPath");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Photos");
        }
    }
}
