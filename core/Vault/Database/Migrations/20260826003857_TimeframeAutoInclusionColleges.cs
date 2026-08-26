using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class TimeframeAutoInclusionColleges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The auto-inclusion college becomes a JSON array of colleges. Default to an empty array (not "", which
            // is not valid JSON), and carry any existing single college over as a one-element array before dropping
            // the old column so no assignment is lost.
            migrationBuilder.AddColumn<string>(
                name: "AutoInclusionColleges",
                table: "Timeframes",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.Sql(
                "UPDATE Timeframes SET AutoInclusionColleges = '[' || AutoInclusionCollege || ']' WHERE AutoInclusionCollege IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "AutoInclusionCollege",
                table: "Timeframes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoInclusionColleges",
                table: "Timeframes");

            migrationBuilder.AddColumn<int>(
                name: "AutoInclusionCollege",
                table: "Timeframes",
                type: "INTEGER",
                nullable: true);
        }
    }
}
