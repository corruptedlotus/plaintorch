using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class OrbitScheduleStateHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_OrbitScheduleStates",
                table: "OrbitScheduleStates");

            migrationBuilder.AlterColumn<string>(
                name: "IncentiveId",
                table: "OrbitScheduleStates",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<long>(
                name: "Id",
                table: "OrbitScheduleStates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Sqlite:Autoincrement", true);

            // Discriminator backfill (PEP100 patch 2): every state stored before the hierarchy existed belongs to an
            // incentive, so the new column fills existing rows with the incentive kind. The SQLite table rebuild this
            // migration triggers then copies it along with IncentiveId, StateJson and UpdatedUtc, and hands each row a
            // fresh autoincrement Id. (A SQL UPDATE here would run while the rebuild is pending, which EF warns about.)
            migrationBuilder.AddColumn<string>(
                name: "Discriminator",
                table: "OrbitScheduleStates",
                type: "TEXT",
                maxLength: 34,
                nullable: false,
                defaultValue: "IncentiveOrbitScheduleState");

            migrationBuilder.AddColumn<long>(
                name: "TimeframeId",
                table: "OrbitScheduleStates",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_OrbitScheduleStates",
                table: "OrbitScheduleStates",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_OrbitScheduleStates_IncentiveId",
                table: "OrbitScheduleStates",
                column: "IncentiveId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrbitScheduleStates_TimeframeId",
                table: "OrbitScheduleStates",
                column: "TimeframeId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OrbitScheduleStates_Timeframes_TimeframeId",
                table: "OrbitScheduleStates",
                column: "TimeframeId",
                principalTable: "Timeframes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The pre-hierarchy table is keyed by IncentiveId alone, so timeframe states have no place in it and go first.
            migrationBuilder.Sql("DELETE FROM \"OrbitScheduleStates\" WHERE \"Discriminator\" <> 'IncentiveOrbitScheduleState' OR \"IncentiveId\" IS NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_OrbitScheduleStates_Timeframes_TimeframeId",
                table: "OrbitScheduleStates");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OrbitScheduleStates",
                table: "OrbitScheduleStates");

            migrationBuilder.DropIndex(
                name: "IX_OrbitScheduleStates_IncentiveId",
                table: "OrbitScheduleStates");

            migrationBuilder.DropIndex(
                name: "IX_OrbitScheduleStates_TimeframeId",
                table: "OrbitScheduleStates");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "OrbitScheduleStates");

            migrationBuilder.DropColumn(
                name: "Discriminator",
                table: "OrbitScheduleStates");

            migrationBuilder.DropColumn(
                name: "TimeframeId",
                table: "OrbitScheduleStates");

            migrationBuilder.AlterColumn<string>(
                name: "IncentiveId",
                table: "OrbitScheduleStates",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_OrbitScheduleStates",
                table: "OrbitScheduleStates",
                column: "IncentiveId");
        }
    }
}
