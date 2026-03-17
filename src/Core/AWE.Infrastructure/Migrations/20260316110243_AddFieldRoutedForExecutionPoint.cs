using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldRoutedForExecutionPoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "routed",
                table: "ExecutionPointers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "routed",
                table: "ExecutionPointers");
        }
    }
}
