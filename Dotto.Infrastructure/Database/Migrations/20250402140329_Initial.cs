using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dotto.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChannelFlags",
                columns: table => new
                {
                    ChannelId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Flags = table.Column<string[]>(type: "text[]", nullable: false),
                    UpdatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelFlags", x => x.ChannelId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelFlags_UpdatedOn",
                table: "ChannelFlags",
                column: "UpdatedOn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelFlags");
        }
    }
}
