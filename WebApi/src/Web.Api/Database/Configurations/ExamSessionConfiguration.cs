using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Web.Api.Common.ValueObjects;
using Web.Api.Features.ExamSessions;
using Web.Api.Features.Exams;
using Web.Api.Features.Users;

namespace Web.Api.Database.Configurations;

internal sealed class ExamSessionConfiguration : IEntityTypeConfiguration<ExamSession>
{
    public void Configure(EntityTypeBuilder<ExamSession> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.OneTimeCodeHash)
            .HasConversion(hash => hash.Value, value => Sha256Hash.FromTrusted(value))
            .HasMaxLength(Sha256Hash.Length)
            .IsFixedLength();

        builder.Property(s => s.PackageSha256)
            .HasConversion(hash => hash.Value, value => Sha256Hash.FromTrusted(value))
            .HasMaxLength(Sha256Hash.Length)
            .IsFixedLength();

        builder.Property(s => s.PackageObjectKey)
            .HasConversion(objectKey => objectKey.Value, value => ObjectKey.FromTrusted(value))
            .HasMaxLength(ObjectKey.MaxLength);

        builder.Property(s => s.HeaderObjectKey)
            .HasConversion(objectKey => objectKey.Value, value => ObjectKey.FromTrusted(value))
            .HasMaxLength(ObjectKey.MaxLength);

        builder.HasIndex(s => s.PackageObjectKey).IsUnique();

        // The student catalog reads sessions by exam, ordered by when they start.
        builder.HasIndex(s => new { s.ExamPackageId, s.StartsAt });

        // Deliberately not unique: a code is 100 bits of randomness, and a unique index here would
        // turn the digest column into an oracle that confirms a guessed code by rejecting it.
        builder.HasIndex(s => s.OneTimeCodeHash);

        builder.HasOne<ExamPackage>().WithMany().HasForeignKey(s => s.ExamPackageId);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.CreatedByProfessorId);
    }
}
