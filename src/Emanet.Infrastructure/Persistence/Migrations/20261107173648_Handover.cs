using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Emanet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Handover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "rating_count",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "rating_sum",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "claim_minor",
                table: "bookings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "claim_note",
                table: "bookings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "claimed_at",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "damage_minor",
                table: "bookings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deposit_settled_at",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "picked_up_at",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pickup_code",
                table: "bookings",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "renter_answer",
                table: "bookings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "renter_answer_note",
                table: "bookings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "return_code",
                table: "bookings",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "returned_at",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "wrong_codes",
                table: "bookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "handover_photos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    uploader_id = table.Column<Guid>(type: "uuid", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_handover_photos", x => x.id);
                    table.ForeignKey(
                        name: "fk_handover_photos_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_handover_photos_users_uploader_id",
                        column: x => x.uploader_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    stars = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reviews", x => x.id);
                    table.CheckConstraint("ck_reviews_stars", "stars BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_reviews_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reviews_listings_listing_id",
                        column: x => x.listing_id,
                        principalTable: "listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_reviews_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reviews_users_subject_user_id",
                        column: x => x.subject_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_open",
                table: "bookings",
                column: "status",
                filter: "status IN ('Confirmed', 'Returned', 'Disputed') OR (status = 'Completed' AND deposit_settled_at IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bookings_claims",
                table: "bookings",
                sql: "claim_minor BETWEEN 0 AND deposit_minor AND damage_minor BETWEEN 0 AND claim_minor");

            migrationBuilder.CreateIndex(
                name: "ix_handover_photos_booking_id_stage_created_at",
                table: "handover_photos",
                columns: new[] { "booking_id", "stage", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_handover_photos_uploader_id",
                table: "handover_photos",
                column: "uploader_id");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_author_id",
                table: "reviews",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_booking_id_author_id",
                table: "reviews",
                columns: new[] { "booking_id", "author_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reviews_listing_id_subject_created_at",
                table: "reviews",
                columns: new[] { "listing_id", "subject", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_subject_user_id_subject_created_at",
                table: "reviews",
                columns: new[] { "subject_user_id", "subject", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "handover_photos");

            migrationBuilder.DropTable(
                name: "reviews");

            migrationBuilder.DropIndex(
                name: "ix_bookings_open",
                table: "bookings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bookings_claims",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "rating_count",
                table: "users");

            migrationBuilder.DropColumn(
                name: "rating_sum",
                table: "users");

            migrationBuilder.DropColumn(
                name: "claim_minor",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "claim_note",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "claimed_at",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "damage_minor",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "deposit_settled_at",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "picked_up_at",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "pickup_code",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "renter_answer",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "renter_answer_note",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "return_code",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "returned_at",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "wrong_codes",
                table: "bookings");
        }
    }
}
