using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UFSWebApp.Migrations
{
    /// <inheritdoc />
    public partial class AddResidenceExitRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "residenceExitRequests",
                columns: table => new
                {
                    ResidenceExitRequestId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoomAllocationId = table.Column<int>(type: "int", nullable: false),
                    userId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    DecisionNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DecidedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DateSubmitted = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateDecided = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_residenceExitRequests", x => x.ResidenceExitRequestId);
                    table.ForeignKey(
                        name: "FK_residenceExitRequests_roomAllocations_RoomAllocationId",
                        column: x => x.RoomAllocationId,
                        principalTable: "roomAllocations",
                        principalColumn: "RoomAllocationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_residenceExitRequests_users_userId",
                        column: x => x.userId,
                        principalTable: "users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_residenceExitRequests_RoomAllocationId_Status",
                table: "residenceExitRequests",
                columns: new[] { "RoomAllocationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_residenceExitRequests_userId",
                table: "residenceExitRequests",
                column: "userId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "residenceExitRequests");
        }
    }
}
