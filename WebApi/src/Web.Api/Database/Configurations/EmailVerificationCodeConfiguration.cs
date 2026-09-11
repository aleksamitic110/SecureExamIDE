using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Web.Api.Features.Users;

namespace Web.Api.Database.Configurations;

internal sealed class EmailVerificationCodeConfiguration : IEntityTypeConfiguration<EmailVerificationCode>
{
    public void Configure(EntityTypeBuilder<EmailVerificationCode> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.CodeHash)
            .HasMaxLength(Sha256HexLength)
            .IsFixedLength();

        // At most one outstanding code per user, enforced by the database as well as the handlers:
        // a resend overwrites the row rather than adding a second one.
        builder.HasIndex(c => c.UserId).IsUnique();

        builder.HasOne<User>().WithMany().HasForeignKey(c => c.UserId);
    }

    private const int Sha256HexLength = 64;
}
