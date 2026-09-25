using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class DatasDosDados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DreAtualizadoEm",
                table: "Clientes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DreDadosDe",
                table: "Clientes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PrecificacaoAtualizadoEm",
                table: "Clientes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PrecificacaoDadosDe",
                table: "Clientes",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DreAtualizadoEm",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "DreDadosDe",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "PrecificacaoAtualizadoEm",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "PrecificacaoDadosDe",
                table: "Clientes");
        }
    }
}
