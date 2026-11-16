using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FrameForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhotoDecisions",
                columns: table => new
                {
                    PhotoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Rating = table.Column<int>(type: "INTEGER", nullable: true),
                    Label = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoDecisions", x => x.PhotoId);
                    table.CheckConstraint("CK_PhotoDecisions_Rating", "\"Rating\" IS NULL OR (\"Rating\" BETWEEN 1 AND 5)");
                    table.ForeignKey(
                        name: "FK_PhotoDecisions_Photos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "Photos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoDecisions_State",
                table: "PhotoDecisions",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhotoDecisions");
        }
    }
}
