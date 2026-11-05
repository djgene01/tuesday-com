using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tuesday.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InviteTableName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Invites",
                table: "Invites");

            migrationBuilder.RenameTable(
                name: "Invites",
                newName: "invites");

            migrationBuilder.AlterColumn<string>(
                name: "TokenHash",
                table: "invites",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "invites",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddPrimaryKey(
                name: "PK_invites",
                table: "invites",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_invites_TenantId_Email",
                table: "invites",
                columns: new[] { "TenantId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_invites_TokenHash",
                table: "invites",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_invites",
                table: "invites");

            migrationBuilder.DropIndex(
                name: "IX_invites_TenantId_Email",
                table: "invites");

            migrationBuilder.DropIndex(
                name: "IX_invites_TokenHash",
                table: "invites");

            migrationBuilder.RenameTable(
                name: "invites",
                newName: "Invites");

            migrationBuilder.AlterColumn<string>(
                name: "TokenHash",
                table: "Invites",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Invites",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Invites",
                table: "Invites",
                column: "Id");
        }
    }
}
