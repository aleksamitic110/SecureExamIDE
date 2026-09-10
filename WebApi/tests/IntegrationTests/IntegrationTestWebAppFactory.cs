using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using Minio.DataModel.Args;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;
using Web.Api;
using Web.Api.Database;

namespace IntegrationTests;

public sealed class IntegrationTestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string ProfessorRegistrationCode = "integration-test-professor-code";

    public const string StorageBucket = "exam-packages";

    private const string StorageImage = "minio/minio:latest";
    private const string StorageAccessKey = "minioadmin";
    private const string StorageSecretKey = "minioadmin";

    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("secure-exam-ide")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly MinioContainer _storageContainer = new MinioBuilder(StorageImage)
        .WithUsername(StorageAccessKey)
        .WithPassword(StorageSecretKey)
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Database", _dbContainer.GetConnectionString());

        // Provide deterministic JWT settings so tokens can be issued and validated in tests.
        builder.UseSetting("Jwt:Secret", "super-duper-secret-value-that-should-be-in-user-secrets");
        builder.UseSetting("Jwt:Issuer", "clean-architecture");
        builder.UseSetting("Jwt:Audience", "developers");
        builder.UseSetting("Jwt:ExpirationInMinutes", "60");

        builder.UseSetting("Registration:ProfessorRegistrationCode", ProfessorRegistrationCode);

        // Point the API at the throwaway MinIO container. GetConnectionString() yields a full URI,
        // but StorageOptions.Endpoint is a host:port pair, so the scheme is stripped here.
        var storageUri = new Uri(_storageContainer.GetConnectionString());
        builder.UseSetting("Storage:Endpoint", storageUri.Authority);
        builder.UseSetting("Storage:AccessKey", StorageAccessKey);
        builder.UseSetting("Storage:SecretKey", StorageSecretKey);
        builder.UseSetting("Storage:Bucket", StorageBucket);
        builder.UseSetting("Storage:UseSsl", "false");

        // Relax rate limiting so the test suite is not throttled.
        builder.UseSetting("RateLimiting:Global:PermitLimit", "100000");
        builder.UseSetting("RateLimiting:Authentication:PermitLimit", "100000");
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_dbContainer.StartAsync(), _storageContainer.StartAsync());

        await CreateStorageBucketAsync();

        using IServiceScope scope = Services.CreateScope();
        ApplicationDbContext dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await _dbContainer.DisposeAsync();
        await _storageContainer.DisposeAsync();
        await base.DisposeAsync();
    }

    // The compose stack creates the bucket with a one-shot mc container; the test container has to
    // do it for itself before the API writes anything.
    private async Task CreateStorageBucketAsync()
    {
        var storageUri = new Uri(_storageContainer.GetConnectionString());

        // Ownership passes to the using declaration; the analyzer cannot see that through the
        // builder chain.
#pragma warning disable CA2000
        using IMinioClient minioClient = new MinioClient()
            .WithEndpoint(storageUri.Authority)
            .WithCredentials(StorageAccessKey, StorageSecretKey)
            .WithSSL(false)
            .Build();
#pragma warning restore CA2000

        bool exists = await minioClient.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(StorageBucket));

        if (!exists)
        {
            await minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(StorageBucket));
        }
    }
}
