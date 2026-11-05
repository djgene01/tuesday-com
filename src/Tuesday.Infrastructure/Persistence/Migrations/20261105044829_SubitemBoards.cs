using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tuesday.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SubitemBoards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subitem_boards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subitem_boards", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subitem_boards_BoardId",
                table: "subitem_boards",
                column: "BoardId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subitem_boards");
        }
    }
}
