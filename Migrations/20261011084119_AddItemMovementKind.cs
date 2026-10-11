using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddItemMovementKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "ItemTransfers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Before this upgrade there was one location, so every existing
            // Reception account worked at what is now the default branch.
            migrationBuilder.Sql(@"
                UPDATE ""AspNetUsers"" SET ""BranchId"" = 1
                WHERE ""BranchId"" IS NULL AND ""Id"" IN (
                    SELECT ur.""UserId"" FROM ""AspNetUserRoles"" ur
                    JOIN ""AspNetRoles"" r ON r.""Id"" = ur.""RoleId""
                    WHERE r.""NormalizedName"" = 'RECEPTION');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ItemTransfers");
        }
    }
}
