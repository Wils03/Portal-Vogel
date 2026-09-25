using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class Comercial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ComercialAtualizadoEm",
                table: "Clientes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ComercialDadosDe",
                table: "Clientes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SqlComercial",
                table: "Clientes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MetasVendedores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClienteId = table.Column<int>(type: "int", nullable: false),
                    Competencia = table.Column<DateOnly>(type: "date", nullable: false),
                    Vendedor = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Meta = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AtualizadoPor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetasVendedores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetasVendedores_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VendasVendedores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClienteId = table.Column<int>(type: "int", nullable: false),
                    Competencia = table.Column<DateOnly>(type: "date", nullable: false),
                    Vendedor = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NomeVendedor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Gerente = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    NomeGerente = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    VendaBruta = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Devolucoes = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Cmv = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Documentos = table.Column<int>(type: "int", nullable: false),
                    Itens = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendasVendedores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendasVendedores_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MetasVendedores_ClienteId_Competencia_Vendedor",
                table: "MetasVendedores",
                columns: new[] { "ClienteId", "Competencia", "Vendedor" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendasVendedores_ClienteId_Competencia",
                table: "VendasVendedores",
                columns: new[] { "ClienteId", "Competencia" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MetasVendedores");

            migrationBuilder.DropTable(
                name: "VendasVendedores");

            migrationBuilder.DropColumn(
                name: "ComercialAtualizadoEm",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "ComercialDadosDe",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "SqlComercial",
                table: "Clientes");
        }
    }
}
