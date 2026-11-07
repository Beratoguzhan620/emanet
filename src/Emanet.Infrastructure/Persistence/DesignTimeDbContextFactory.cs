using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Emanet.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c>; generating migrations does not open a connection.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EmanetDbContext>
{
    public EmanetDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<EmanetDbContext>();
        DependencyInjection.ConfigureDbContext(options, DependencyInjection.BuildDataSource("Host=localhost;Database=emanet_design"));
        return new EmanetDbContext(options.Options);
    }
}
