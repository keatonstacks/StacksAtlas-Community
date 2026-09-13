using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StacksAtlas.Core.Data.Hub;

public class HubDbContextFactory : IDesignTimeDbContextFactory<HubDbContext>
{
    public HubDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<HubDbContext>();
        optionsBuilder.UseSqlite("Data Source=StacksAtlas.Hub.db");

        return new HubDbContext(optionsBuilder.Options);
    }
}
