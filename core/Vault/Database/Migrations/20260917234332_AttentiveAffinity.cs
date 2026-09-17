using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class AttentiveAffinity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AffinityTimeframeId",
                table: "Attentives",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attentives_AffinityTimeframeId",
                table: "Attentives",
                column: "AffinityTimeframeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Attentives_Timeframes_AffinityTimeframeId",
                table: "Attentives",
                column: "AffinityTimeframeId",
                principalTable: "Timeframes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attentives_Timeframes_AffinityTimeframeId",
                table: "Attentives");

            migrationBuilder.DropIndex(
                name: "IX_Attentives_AffinityTimeframeId",
                table: "Attentives");

            migrationBuilder.DropColumn(
                name: "AffinityTimeframeId",
                table: "Attentives");
        }
    }
}
