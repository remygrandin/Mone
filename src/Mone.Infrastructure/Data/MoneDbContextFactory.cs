using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mone.Infrastructure.Data;

public sealed class MoneDbContextFactory : IDesignTimeDbContextFactory<MoneDbContext>
{
    public MoneDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("MONE_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=mone;Username=postgres;Password=mone_dev";

        var options = new DbContextOptionsBuilder<MoneDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new MoneDbContext(options);
    }
}
