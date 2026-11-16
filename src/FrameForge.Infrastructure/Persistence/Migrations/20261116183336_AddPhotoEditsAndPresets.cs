using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FrameForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoEditsAndPresets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EditPresets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false, collation: "NOCASE"),
                    Groups = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    RecipeJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditPresets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PhotoEdits",
                columns: table => new
                {
                    PhotoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    RecipeJson = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoEdits", x => x.PhotoId);
                    table.ForeignKey(
                        name: "FK_PhotoEdits_Photos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "Photos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EditPresets_Name",
                table: "EditPresets",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EditPresets");

            migrationBuilder.DropTable(
                name: "PhotoEdits");
        }
    }
}
