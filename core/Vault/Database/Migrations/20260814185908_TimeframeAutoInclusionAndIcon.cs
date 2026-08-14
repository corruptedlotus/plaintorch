using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class TimeframeAutoInclusionAndIcon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AutoInclusion",
                table: "Timeframes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AutoInclusionCollege",
                table: "Timeframes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Icon",
                table: "Timeframes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AffinityTimeframeId",
                table: "Reflective",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reflective_AffinityTimeframeId",
                table: "Reflective",
                column: "AffinityTimeframeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Reflective_Timeframes_AffinityTimeframeId",
                table: "Reflective",
                column: "AffinityTimeframeId",
                principalTable: "Timeframes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reflective_Timeframes_AffinityTimeframeId",
                table: "Reflective");

            migrationBuilder.DropIndex(
                name: "IX_Reflective_AffinityTimeframeId",
                table: "Reflective");

            migrationBuilder.DropColumn(
                name: "AutoInclusion",
                table: "Timeframes");

            migrationBuilder.DropColumn(
                name: "AutoInclusionCollege",
                table: "Timeframes");

            migrationBuilder.DropColumn(
                name: "Icon",
                table: "Timeframes");

            migrationBuilder.DropColumn(
                name: "AffinityTimeframeId",
                table: "Reflective");
        }
    }
}
