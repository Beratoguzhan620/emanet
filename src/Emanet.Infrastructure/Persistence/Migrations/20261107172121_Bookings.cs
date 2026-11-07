using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Emanet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Bookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.AddColumn<bool>(
                name: "instant_book",
                table: "listings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    renter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    days = table.Column<int>(type: "integer", nullable: false),
                    rent_minor = table.Column<long>(type: "bigint", nullable: false),
                    fee_minor = table.Column<long>(type: "bigint", nullable: false),
                    deposit_minor = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    rent_hold_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    deposit_hold_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    refund_minor = table.Column<long>(type: "bigint", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    respond_by = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rent_charged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    holds_released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    refunded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookings", x => x.id);
                    table.CheckConstraint("ck_bookings_amounts", "rent_minor > 0 AND fee_minor >= 0 AND fee_minor <= rent_minor AND deposit_minor >= 0 AND refund_minor BETWEEN 0 AND rent_minor");
                    table.CheckConstraint("ck_bookings_end_after_start", "end_date > start_date");
                    table.ForeignKey(
                        name: "fk_bookings_listings_listing_id",
                        column: x => x.listing_id,
                        principalTable: "listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bookings_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bookings_users_renter_id",
                        column: x => x.renter_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    memo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "booking_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    detail = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_booking_events_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar", x => x.id);
                    table.CheckConstraint("ck_calendar_end_after_start", "end_date > start_date");
                    table.ForeignKey(
                        name: "fk_calendar_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_calendar_listings_listing_id",
                        column: x => x.listing_id,
                        principalTable: "listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ledger_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_lines", x => x.id);
                    table.CheckConstraint("ck_ledger_lines_not_zero", "amount <> 0");
                    table.ForeignKey(
                        name: "fk_ledger_lines_journal_entry_id",
                        column: x => x.entry_id,
                        principalTable: "journal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_booking_events_booking_id_id",
                table: "booking_events",
                columns: new[] { "booking_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_listing_id_status",
                table: "bookings",
                columns: new[] { "listing_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_owner_id_requested_at",
                table: "bookings",
                columns: new[] { "owner_id", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_renter_id_requested_at",
                table: "bookings",
                columns: new[] { "renter_id", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_respond_by",
                table: "bookings",
                column: "respond_by",
                filter: "status = 'Requested'");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_booking_id",
                table: "calendar",
                column: "booking_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_calendar_listing_id",
                table: "calendar",
                column: "listing_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_booking_id",
                table: "journal",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_key",
                table: "journal",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ledger_lines_account_id",
                table: "ledger_lines",
                columns: new[] { "account", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_lines_entry_id",
                table: "ledger_lines",
                column: "entry_id");

            // No two calendar rows of a listing may share a day. This is what makes double booking impossible,
            // whatever the application does or however many requests arrive at once.
            migrationBuilder.Sql($"""
                ALTER TABLE calendar ADD CONSTRAINT {Configurations.CalendarEntryConfiguration.NoOverlap}
                    EXCLUDE USING gist (listing_id WITH =, daterange(start_date, end_date, '[)') WITH &&);
                """);

            // Every journal entry sums to zero, checked when the transaction commits, after all its lines are in.
            migrationBuilder.Sql("""
                CREATE FUNCTION ledger_entry_balances() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF (SELECT coalesce(sum(amount), 0) FROM ledger_lines WHERE entry_id = NEW.entry_id) <> 0 THEN
                        RAISE EXCEPTION 'journal entry % does not balance', NEW.entry_id USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NULL;
                END $$;

                CREATE CONSTRAINT TRIGGER ledger_lines_balance AFTER INSERT ON ledger_lines
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION ledger_entry_balances();
                """);

            // The books are append-only: a mistake is corrected by a new entry, never by changing an old one.
            migrationBuilder.Sql("""
                CREATE FUNCTION ledger_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION '% is append-only', TG_TABLE_NAME USING ERRCODE = 'insufficient_privilege';
                END $$;

                CREATE TRIGGER ledger_lines_append_only BEFORE UPDATE OR DELETE ON ledger_lines
                    FOR EACH ROW EXECUTE FUNCTION ledger_append_only();
                CREATE TRIGGER journal_append_only BEFORE UPDATE OR DELETE ON journal
                    FOR EACH ROW EXECUTE FUNCTION ledger_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS journal_append_only ON journal;
                DROP TRIGGER IF EXISTS ledger_lines_append_only ON ledger_lines;
                DROP TRIGGER IF EXISTS ledger_lines_balance ON ledger_lines;
                DROP FUNCTION IF EXISTS ledger_append_only();
                DROP FUNCTION IF EXISTS ledger_entry_balances();
                """);

            migrationBuilder.DropTable(
                name: "booking_events");

            migrationBuilder.DropTable(
                name: "calendar");

            migrationBuilder.DropTable(
                name: "ledger_lines");

            migrationBuilder.DropTable(
                name: "bookings");

            migrationBuilder.DropTable(
                name: "journal");

            migrationBuilder.DropColumn(
                name: "instant_book",
                table: "listings");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");
        }
    }
}
