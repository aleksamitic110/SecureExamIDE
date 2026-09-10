using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Web.Api.Common.ValueObjects;
using Web.Api.Features.Devices;
using Web.Api.Features.Users;

namespace Web.Api.Database.Configurations;

internal sealed class DeviceCredentialConfiguration : IEntityTypeConfiguration<DeviceCredential>
{
    public void Configure(EntityTypeBuilder<DeviceCredential> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.DeviceName)
            .HasConversion(name => name.Value, value => DeviceName.FromTrusted(value))
            .HasMaxLength(DeviceName.MaxLength);

        builder.Property(d => d.SecretHash).HasMaxLength(SecretHashMaxLength);

        // Credentials are looked up by the hash of the presented secret, so this index is the
        // access path for every device-token exchange, not merely a uniqueness guard.
        builder.HasIndex(d => d.SecretHash).IsUnique();

        builder.HasIndex(d => d.UserId);

        builder.HasOne<User>().WithMany().HasForeignKey(d => d.UserId);
    }

    private const int SecretHashMaxLength = 64;
}
