using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class TimeframeExclusiveAndDirectiveAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Exclusive",
                table: "Timeframes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "AvailabilityTimeframeId",
                table: "Directives",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Directives_AvailabilityTimeframeId",
                table: "Directives",
                column: "AvailabilityTimeframeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Directives_Timeframes_AvailabilityTimeframeId",
                table: "Directives",
                column: "AvailabilityTimeframeId",
                principalTable: "Timeframes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Directives_Timeframes_AvailabilityTimeframeId",
                table: "Directives");

            migrationBuilder.DropIndex(
                name: "IX_Directives_AvailabilityTimeframeId",
                table: "Directives");

            migrationBuilder.DropColumn(
                name: "Exclusive",
                table: "Timeframes");

            migrationBuilder.DropColumn(
                name: "AvailabilityTimeframeId",
                table: "Directives");
        }
    }
}
