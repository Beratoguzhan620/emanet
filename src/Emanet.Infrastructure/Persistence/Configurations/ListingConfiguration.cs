using Emanet.Core.Listings;
using Emanet.Core.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace Emanet.Infrastructure.Persistence.Configurations;

internal sealed class ListingConfiguration : IEntityTypeConfiguration<Listing>
{
    /// <summary>
    /// The full-text column: every word twice, stemmed as Turkish (so a plural finds a singular) and as written (so the
    /// start of a word finds it). The title weighs more than the description.
    /// </summary>
    public const string SearchColumn = "Search";

    public void Configure(EntityTypeBuilder<Listing> builder)
    {
        builder.ToTable("listings");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Title).HasMaxLength(Listing.TitleMax);
        builder.Property(l => l.Description).HasMaxLength(Listing.DescriptionMax);
        builder.Property(l => l.Area).HasMaxLength(Listing.AreaMax);
        builder.Property(l => l.Category).HasConversion<string>().HasMaxLength(16);
        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(l => l.Location).HasColumnType("geography (point, 4326)");
        builder.Property(l => l.PublicLocation).HasColumnType("geography (point, 4326)");
        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(l => l.Rating);
        builder.Property<NpgsqlTsVector>(SearchColumn)
            .HasColumnName("search")
            .HasComputedColumnSql(
                "setweight(to_tsvector('turkish', title), 'A') || setweight(to_tsvector('simple', title), 'A') || " +
                "setweight(to_tsvector('turkish', description), 'B') || setweight(to_tsvector('simple', description), 'B')",
                stored: true);
        builder.HasIndex(SearchColumn).HasMethod("gin");
        builder.HasIndex(l => l.PublicLocation).HasMethod("gist");
        builder.HasIndex(l => new { l.Status, l.Category });
        builder.HasIndex(l => new { l.OwnerId, l.CreatedAt });
        builder.HasOne<User>().WithMany().HasForeignKey(l => l.OwnerId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ListingPhotoConfiguration : IEntityTypeConfiguration<ListingPhoto>
{
    public void Configure(EntityTypeBuilder<ListingPhoto> builder)
    {
        builder.ToTable("listing_photos");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Ignore(p => p.FullKey);
        builder.Ignore(p => p.ThumbKey);
        builder.HasOne<Listing>().WithMany().HasForeignKey(p => p.ListingId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => new { p.ListingId, p.Position });
    }
}
