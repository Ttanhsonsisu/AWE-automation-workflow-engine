using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldSuportLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "worker_id",
                table: "ExecutionLog",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "worker_id",
                table: "ExecutionLog");
        }
    }
}
