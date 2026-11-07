using Emanet.Core.Bookings;
using Emanet.Core.Ledger;
using Emanet.Core.Listings;
using Emanet.Core.Reviews;
using Emanet.Core.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Emanet.Infrastructure.Persistence.Configurations;

internal sealed class CalendarEntryConfiguration : IEntityTypeConfiguration<CalendarEntry>
{
    /// <summary>Created in the migration: EF Core has no way to describe an exclusion constraint.</summary>
    public const string NoOverlap = "ex_calendar_no_overlap";

    public void Configure(EntityTypeBuilder<CalendarEntry> builder)
    {
        builder.ToTable("calendar", t => t.HasCheckConstraint("ck_calendar_end_after_start", "end_date > start_date"));
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Start).HasColumnName("start_date");
        builder.Property(c => c.End).HasColumnName("end_date");
        builder.Property(c => c.Note).HasMaxLength(CalendarEntry.NoteMax);
        builder.HasOne<Listing>().WithMany().HasForeignKey(c => c.ListingId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Booking>().WithOne().HasForeignKey<CalendarEntry>(c => c.BookingId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => c.BookingId).IsUnique();
    }
}

internal sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("bookings", t =>
        {
            t.HasCheckConstraint("ck_bookings_end_after_start", "end_date > start_date");
            t.HasCheckConstraint("ck_bookings_amounts", "rent_minor > 0 AND fee_minor >= 0 AND fee_minor <= rent_minor AND deposit_minor >= 0 AND refund_minor BETWEEN 0 AND rent_minor");
            t.HasCheckConstraint("ck_bookings_claims", "claim_minor BETWEEN 0 AND deposit_minor AND damage_minor BETWEEN 0 AND claim_minor");
        });
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.Start).HasColumnName("start_date");
        builder.Property(b => b.End).HasColumnName("end_date");
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(12);
        builder.Property(b => b.Message).HasMaxLength(Booking.MessageMax);
        builder.Property(b => b.Reason).HasMaxLength(Booking.ReasonMax);
        builder.Property(b => b.PaymentMethod).HasMaxLength(64);
        builder.Property(b => b.RentHoldId).HasMaxLength(64);
        builder.Property(b => b.DepositHoldId).HasMaxLength(64);
        builder.Property(b => b.PickupCode).HasMaxLength(6);
        builder.Property(b => b.ReturnCode).HasMaxLength(6);
        builder.Property(b => b.ClaimNote).HasMaxLength(Booking.ClaimNoteMax);
        builder.Property(b => b.RenterAnswer).HasConversion<string>().HasMaxLength(10);
        builder.Property(b => b.RenterAnswerNote).HasMaxLength(Booking.ClaimNoteMax);
        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(b => b.ClaimBy);
        builder.Ignore(b => b.AnswerBy);
        builder.Ignore(b => b.OwesDepositSettlement);
        builder.Ignore(b => b.Holds);
        builder.Ignore(b => b.RevealsPlace);
        builder.Ignore(b => b.OwnerShareMinor);
        builder.Ignore(b => b.OwesRentCharge);
        builder.Ignore(b => b.OwesRefund);
        builder.Ignore(b => b.OwesRelease);
        builder.HasOne<Listing>().WithMany().HasForeignKey(b => b.ListingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(b => b.RenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(b => b.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(b => new { b.RenterId, b.RequestedAt });
        builder.HasIndex(b => new { b.OwnerId, b.RequestedAt });
        builder.HasIndex(b => new { b.ListingId, b.Status });
        // The sweeper's work: requests that ran out, rentals the clock settles, and deposits still to settle.
        builder.HasIndex(b => b.RespondBy).HasFilter("status = 'Requested'");
        builder.HasIndex(b => b.Status).HasDatabaseName("ix_bookings_open").HasFilter("status IN ('Confirmed', 'Returned', 'Disputed') OR (status = 'Completed' AND deposit_settled_at IS NULL)");
    }
}

internal sealed class BookingEventConfiguration : IEntityTypeConfiguration<BookingEvent>
{
    public void Configure(EntityTypeBuilder<BookingEvent> builder)
    {
        builder.ToTable("booking_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).UseIdentityAlwaysColumn();
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Detail).HasMaxLength(300);
        builder.HasOne<Booking>().WithMany().HasForeignKey(e => e.BookingId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.BookingId, e.Id });
    }
}

internal sealed class JournalConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.ToTable("journal");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();
        builder.Property(j => j.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(j => j.Key).HasMaxLength(120);
        builder.Property(j => j.Memo).HasMaxLength(200);
        builder.HasIndex(j => j.Key).IsUnique();
        builder.HasIndex(j => j.BookingId);
        builder.HasMany(j => j.Lines).WithOne().HasForeignKey(l => l.EntryId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(j => j.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class LedgerLineConfiguration : IEntityTypeConfiguration<LedgerLine>
{
    public void Configure(EntityTypeBuilder<LedgerLine> builder)
    {
        builder.ToTable("ledger_lines", t => t.HasCheckConstraint("ck_ledger_lines_not_zero", "amount <> 0"));
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).UseIdentityAlwaysColumn();
        builder.Property(l => l.Account).HasMaxLength(80);
        builder.HasIndex(l => new { l.Account, l.Id });
    }
}

internal sealed class HandoverPhotoConfiguration : IEntityTypeConfiguration<HandoverPhoto>
{
    public void Configure(EntityTypeBuilder<HandoverPhoto> builder)
    {
        builder.ToTable("handover_photos");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Stage).HasConversion<string>().HasMaxLength(8);
        builder.HasOne<Booking>().WithMany().HasForeignKey(p => p.BookingId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UploaderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.BookingId, p.Stage, p.CreatedAt });
    }
}

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("reviews", t => t.HasCheckConstraint("ck_reviews_stars", "stars BETWEEN 1 AND 5"));
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Subject).HasConversion<string>().HasMaxLength(8);
        builder.Property(r => r.Comment).HasMaxLength(Review.CommentMax);
        builder.HasOne<Booking>().WithMany().HasForeignKey(r => r.BookingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Listing>().WithMany().HasForeignKey(r => r.ListingId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.AuthorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.SubjectUserId).OnDelete(DeleteBehavior.Restrict);
        // One review per side per rental; a second attempt is refused by the database, not just the code.
        builder.HasIndex(r => new { r.BookingId, r.AuthorId }).IsUnique();
        builder.HasIndex(r => new { r.ListingId, r.Subject, r.CreatedAt });
        builder.HasIndex(r => new { r.SubjectUserId, r.Subject, r.CreatedAt });
    }
}
