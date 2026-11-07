using Emanet.Core.Listings;
using Emanet.Core.Users;
using Microsoft.EntityFrameworkCore;

namespace Emanet.Infrastructure.Persistence;

/// <summary>Accounts and the things people rent out.</summary>
public sealed class EmanetDbContext(DbContextOptions<EmanetDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Listing> Listings => Set<Listing>();

    public DbSet<ListingPhoto> ListingPhotos => Set<ListingPhoto>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("postgis");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EmanetDbContext).Assembly);
    }
}
