using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddKioskPin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "KioskPinFailedAttempts",
                table: "Borrowers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "KioskPinHash",
                table: "Borrowers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "KioskPinLockedUntilUtc",
                table: "Borrowers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "KioskPinSetAtUtc",
                table: "Borrowers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KioskPinFailedAttempts",
                table: "Borrowers");

            migrationBuilder.DropColumn(
                name: "KioskPinHash",
                table: "Borrowers");

            migrationBuilder.DropColumn(
                name: "KioskPinLockedUntilUtc",
                table: "Borrowers");

            migrationBuilder.DropColumn(
                name: "KioskPinSetAtUtc",
                table: "Borrowers");
        }
    }
}
