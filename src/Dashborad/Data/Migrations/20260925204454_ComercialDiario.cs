using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class ComercialDiario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VendasVendedores_ClienteId_Competencia",
                table: "VendasVendedores");

            migrationBuilder.AddColumn<DateOnly>(
                name: "Data",
                table: "VendasVendedores",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.CreateTable(
                name: "DiasUteis",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClienteId = table.Column<int>(type: "int", nullable: false),
                    Competencia = table.Column<DateOnly>(type: "date", nullable: false),
                    DiasUteis = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiasUteis", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiasUteis_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Feriados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClienteId = table.Column<int>(type: "int", nullable: false),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Feriados", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Feriados_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VendasVendedores_ClienteId_Data",
                table: "VendasVendedores",
                columns: new[] { "ClienteId", "Data" });

            migrationBuilder.CreateIndex(
                name: "IX_DiasUteis_ClienteId_Competencia",
                table: "DiasUteis",
                columns: new[] { "ClienteId", "Competencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Feriados_ClienteId_Data",
                table: "Feriados",
                columns: new[] { "ClienteId", "Data" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiasUteis");

            migrationBuilder.DropTable(
                name: "Feriados");

            migrationBuilder.DropIndex(
                name: "IX_VendasVendedores_ClienteId_Data",
                table: "VendasVendedores");

            migrationBuilder.DropColumn(
                name: "Data",
                table: "VendasVendedores");

            migrationBuilder.CreateIndex(
                name: "IX_VendasVendedores_ClienteId_Competencia",
                table: "VendasVendedores",
                columns: new[] { "ClienteId", "Competencia" });
        }
    }
}
