using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MyBank.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "mybank");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "mybank",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    initial_balance = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    balance = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    last_sequence = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.id);
                    table.CheckConstraint("ck_accounts_balance", "balance >= 0");
                    table.CheckConstraint("ck_accounts_initial_balance", "initial_balance >= 0");
                });

            migrationBuilder.CreateTable(
                name: "daily_balances",
                schema: "mybank",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    opening_balance = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    total_credits = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    total_debits = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    closing_balance = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    entry_count = table.Column<int>(type: "integer", nullable: false),
                    first_sequence = table.Column<long>(type: "bigint", nullable: false),
                    last_sequence = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_balances", x => new { x.account_id, x.date });
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                schema: "mybank",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_ledger_entries_amount", "amount > 0");
                    table.CheckConstraint("ck_ledger_entries_balance_after", "balance_after >= 0");
                    table.ForeignKey(
                        name: "FK_ledger_entries_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "mybank",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "mybank",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_outbox_messages_ledger_entries_entry_id",
                        column: x => x.entry_id,
                        principalSchema: "mybank",
                        principalTable: "ledger_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_owner_id",
                schema: "mybank",
                table: "accounts",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_account_id_idempotency_key",
                schema: "mybank",
                table: "ledger_entries",
                columns: new[] { "account_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_account_id_recorded_at_sequence",
                schema: "mybank",
                table: "ledger_entries",
                columns: new[] { "account_id", "recorded_at", "sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_account_id_sequence",
                schema: "mybank",
                table: "ledger_entries",
                columns: new[] { "account_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_entry_id",
                schema: "mybank",
                table: "outbox_messages",
                column: "entry_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "mybank",
                table: "outbox_messages",
                column: "id",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending_account",
                schema: "mybank",
                table: "outbox_messages",
                column: "account_id",
                filter: "processed_at IS NULL");

            // Razão imutável: correções são novos lançamentos, nunca UPDATE ou DELETE.
            migrationBuilder.Sql("""
                CREATE FUNCTION mybank.prevent_ledger_entry_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'ledger_entries é imutável: % não permitido', TG_OP
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;

                CREATE TRIGGER trg_ledger_entries_immutable
                    BEFORE UPDATE OR DELETE ON mybank.ledger_entries
                    FOR EACH ROW EXECUTE FUNCTION mybank.prevent_ledger_entry_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_ledger_entries_immutable ON mybank.ledger_entries;
                DROP FUNCTION IF EXISTS mybank.prevent_ledger_entry_mutation();
                """);

            migrationBuilder.DropTable(
                name: "daily_balances",
                schema: "mybank");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "mybank");

            migrationBuilder.DropTable(
                name: "ledger_entries",
                schema: "mybank");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "mybank");
        }
    }
}
