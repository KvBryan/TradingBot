using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebhookListener.Migrations
{
    /// <inheritdoc />
    public partial class AddDealReferenceToTrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DealReference",
                table: "Trades",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DealReference",
                table: "Trades");
        }
    }
}
