using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sonrisa.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty. "xmin" is PostgreSQL's built-in row version, present on every table: EF Core only
            // needs it in the model (concurrency token on Delivery). The generated AddColumn would fail because the
            // name belongs to a system column, so it was removed by hand (S4 review 3).
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: see Up.
        }
    }
}
