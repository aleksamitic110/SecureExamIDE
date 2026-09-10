using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Web.Api.Common.ValueObjects;
using Web.Api.Features.Users;

namespace Web.Api.Database.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        // Each converter stores the underlying string and rehydrates through FromTrusted, which
        // skips revalidation: rows in our own database are already known to be well-formed.
        builder.Property(u => u.Email)
            .HasConversion(email => email.Value, value => Email.FromTrusted(value))
            .HasMaxLength(Email.MaxLength);

        builder.Property(u => u.FirstName)
            .HasConversion(name => name.Value, value => PersonName.FromTrusted(value))
            .HasMaxLength(PersonName.MaxLength);

        builder.Property(u => u.LastName)
            .HasConversion(name => name.Value, value => PersonName.FromTrusted(value))
            .HasMaxLength(PersonName.MaxLength);

        builder.Property(u => u.IndexNumber)
            .HasConversion(
                indexNumber => indexNumber!.Value,
                value => Web.Api.Common.ValueObjects.IndexNumber.FromTrusted(value))
            .HasMaxLength(Web.Api.Common.ValueObjects.IndexNumber.MaxLength);

        builder.Property(u => u.Role)
            .HasConversion<string>()
            .HasMaxLength(RoleMaxLength);

        builder.Property(u => u.IsActive).HasDefaultValue(true);

        builder.HasIndex(u => u.Email).IsUnique();

        // Only students carry an index number, so the uniqueness constraint is filtered to the
        // rows that actually have one - otherwise every professor would collide on NULL.
        builder.HasIndex(u => u.IndexNumber)
            .IsUnique()
            .HasFilter("index_number IS NOT NULL");
    }

    private const int RoleMaxLength = 20;
}
