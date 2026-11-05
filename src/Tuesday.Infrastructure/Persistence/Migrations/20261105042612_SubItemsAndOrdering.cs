using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tuesday.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SubItemsAndOrdering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentItemId",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_items_ParentItemId_Position",
                table: "items",
                columns: new[] { "ParentItemId", "Position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_items_ParentItemId_Position",
                table: "items");

            migrationBuilder.DropColumn(
                name: "ParentItemId",
                table: "items");
        }
    }
}
