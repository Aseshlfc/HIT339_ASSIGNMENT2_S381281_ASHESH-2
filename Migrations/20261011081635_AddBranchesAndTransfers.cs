using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchesAndTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "Loans",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "Items",
                type: "INTEGER",
                nullable: false,
                // Existing items are assigned to the default branch (Id 1,
                // "Central Library"), which is inserted further down.
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItemTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    FromBranchId = table.Column<int>(type: "INTEGER", nullable: false),
                    ToBranchId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RequestedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DispatchedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DispatchedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClosedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ClosureNote = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemTransfers_Branches_FromBranchId",
                        column: x => x.FromBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemTransfers_Branches_ToBranchId",
                        column: x => x.ToBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemTransfers_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Branches",
                columns: new[] { "Id", "Address", "Code", "IsActive", "Name", "Phone" },
                values: new object[] { 1, "1 Library Square, Springfield", "CEN", true, "Central Library", "0800000001" });

            migrationBuilder.CreateIndex(
                name: "IX_Loans_BranchId",
                table: "Loans",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_BranchId",
                table: "Items",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_BranchId",
                table: "AspNetUsers",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_Code",
                table: "Branches",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemTransfers_FromBranchId",
                table: "ItemTransfers",
                column: "FromBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemTransfers_OpenItem",
                table: "ItemTransfers",
                column: "ItemId",
                unique: true,
                filter: "\"Status\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_ItemTransfers_ToBranchId",
                table: "ItemTransfers",
                column: "ToBranchId");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Branches_BranchId",
                table: "AspNetUsers",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Items_Branches_BranchId",
                table: "Items",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Loans_Branches_BranchId",
                table: "Loans",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Branches_BranchId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Items_Branches_BranchId",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_Loans_Branches_BranchId",
                table: "Loans");

            migrationBuilder.DropTable(
                name: "ItemTransfers");

            migrationBuilder.DropTable(
                name: "Branches");

            migrationBuilder.DropIndex(
                name: "IX_Loans_BranchId",
                table: "Loans");

            migrationBuilder.DropIndex(
                name: "IX_Items_BranchId",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_BranchId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "AspNetUsers");
        }
    }
}
