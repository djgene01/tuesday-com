using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tuesday.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AutomationSteps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "automation_steps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AutomationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    ActionValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_steps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_automation_steps_automations_AutomationId",
                        column: x => x.AutomationId,
                        principalTable: "automations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_automation_steps_AutomationId_Position",
                table: "automation_steps",
                columns: new[] { "AutomationId", "Position" },
                unique: true);

            // Every existing rule becomes a one-step chain, so the backfill and the executor
            // agree on where the actions live.
            migrationBuilder.Sql("""
                insert into automation_steps ("Id", "TenantId", "AutomationId", "Position", "Action", "ActionValue")
                select gen_random_uuid(), a."TenantId", a."Id", 0, a."Action", a."ActionValue"
                from automations a
                where not exists (
                    select 1 from automation_steps s where s."AutomationId" = a."Id");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "automation_steps");
        }
    }
}
