using Emanet.Core.Bookings;
using Emanet.Core.Ledger;
using Emanet.Core.Listings;
using Emanet.Core.Users;
using Microsoft.EntityFrameworkCore;

namespace Emanet.Infrastructure.Persistence;

/// <summary>Accounts, the things people rent out, their calendars, rentals and the books.</summary>
public sealed class EmanetDbContext(DbContextOptions<EmanetDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Listing> Listings => Set<Listing>();

    public DbSet<ListingPhoto> ListingPhotos => Set<ListingPhoto>();

    public DbSet<CalendarEntry> Calendar => Set<CalendarEntry>();

    public DbSet<Booking> Bookings => Set<Booking>();

    public DbSet<BookingEvent> BookingEvents => Set<BookingEvent>();

    public DbSet<JournalEntry> Journal => Set<JournalEntry>();

    public DbSet<LedgerLine> LedgerLines => Set<LedgerLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("postgis");
        // Lets one exclusion constraint compare a listing id for equality and a date range for overlap.
        modelBuilder.HasPostgresExtension("btree_gist");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EmanetDbContext).Assembly);
    }
}
