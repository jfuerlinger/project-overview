using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectOverview.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkPackageHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                table: "WorkPackages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkPackages_ParentId",
                table: "WorkPackages",
                column: "ParentId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkPackages_WorkPackages_ParentId",
                table: "WorkPackages",
                column: "ParentId",
                principalTable: "WorkPackages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkPackages_WorkPackages_ParentId",
                table: "WorkPackages");

            migrationBuilder.DropIndex(
                name: "IX_WorkPackages_ParentId",
                table: "WorkPackages");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "WorkPackages");
        }
    }
}
