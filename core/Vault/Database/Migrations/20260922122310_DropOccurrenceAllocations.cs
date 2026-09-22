using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class DropOccurrenceAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Estimation",
                table: "Eventives");

            migrationBuilder.DropColumn(
                name: "Maximum",
                table: "Eventives");

            migrationBuilder.DropColumn(
                name: "Minimum",
                table: "Eventives");

            migrationBuilder.DropColumn(
                name: "Estimation",
                table: "Attentives");

            migrationBuilder.DropColumn(
                name: "Maximum",
                table: "Attentives");

            migrationBuilder.DropColumn(
                name: "Minimum",
                table: "Attentives");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Estimation",
                table: "Eventives",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Maximum",
                table: "Eventives",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Minimum",
                table: "Eventives",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Estimation",
                table: "Attentives",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Maximum",
                table: "Attentives",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Minimum",
                table: "Attentives",
                type: "INTEGER",
                nullable: true);
        }
    }
}
