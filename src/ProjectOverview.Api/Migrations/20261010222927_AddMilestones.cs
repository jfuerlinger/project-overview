using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectOverview.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMilestones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MilestoneId",
                table: "WorkPackages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Milestones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    TargetDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Milestones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Milestones_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkPackages_MilestoneId",
                table: "WorkPackages",
                column: "MilestoneId");

            migrationBuilder.CreateIndex(
                name: "IX_Milestones_ProjectId_TargetDate",
                table: "Milestones",
                columns: new[] { "ProjectId", "TargetDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_WorkPackages_Milestones_MilestoneId",
                table: "WorkPackages",
                column: "MilestoneId",
                principalTable: "Milestones",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkPackages_Milestones_MilestoneId",
                table: "WorkPackages");

            migrationBuilder.DropTable(
                name: "Milestones");

            migrationBuilder.DropIndex(
                name: "IX_WorkPackages_MilestoneId",
                table: "WorkPackages");

            migrationBuilder.DropColumn(
                name: "MilestoneId",
                table: "WorkPackages");
        }
    }
}
