using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Web.Api.Database;

namespace Web.Api.UnitTests.Abstractions;

public abstract class BaseHandlerTest
{
    protected static ApplicationDbContext CreateDbContext(IDomainEventsDispatcher? domainEventsDispatcher = null)
    {
        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"vertical-slice-{Guid.NewGuid()}")
            .Options;

        return new ApplicationDbContext(options, domainEventsDispatcher ?? Substitute.For<IDomainEventsDispatcher>());
    }

    protected static HybridCache CreateCache()
    {
        var services = new ServiceCollection();
#pragma warning disable EXTEXP0018
        services.AddHybridCache();
#pragma warning restore EXTEXP0018
        return services.BuildServiceProvider().GetRequiredService<HybridCache>();
    }
}
