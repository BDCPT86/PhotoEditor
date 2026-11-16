using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FrameForge.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> tooling to create migrations. The WPF app is not a
/// suitable startup project for the tools, so the context is constructed here directly.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FrameForgeDbContext>
{
    public FrameForgeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FrameForgeDbContext>()
            .UseSqlite("Data Source=frameforge.design.db")
            .Options;

        return new FrameForgeDbContext(options);
    }
}
