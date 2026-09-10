using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Web.Api.Common.ValueObjects;
using Web.Api.Features.Exams;

namespace Web.Api.Database.Configurations;

internal sealed class ExamFileConfiguration : IEntityTypeConfiguration<ExamFile>
{
    public void Configure(EntityTypeBuilder<ExamFile> builder)
    {
        builder.HasKey(f => f.Id);

        builder.Property(f => f.FileName)
            .HasConversion(fileName => fileName.Value, value => FileName.FromTrusted(value))
            .HasMaxLength(FileName.MaxLength);

        builder.Property(f => f.ContentType)
            .HasConversion(contentType => contentType.Value, value => ContentType.FromTrusted(value))
            .HasMaxLength(ContentType.MaxLength);

        builder.Property(f => f.ObjectKey)
            .HasConversion(objectKey => objectKey.Value, value => ObjectKey.FromTrusted(value))
            .HasMaxLength(ObjectKey.MaxLength);

        builder.Property(f => f.Sha256)
            .HasConversion(sha256 => sha256.Value, value => Sha256Hash.FromTrusted(value))
            .HasMaxLength(Sha256Hash.Length)
            .IsFixedLength();

        // One stored object backs at most one row, so committing the same upload twice is rejected
        // by the database itself rather than only by the handler's check.
        builder.HasIndex(f => f.ObjectKey).IsUnique();

        builder.HasIndex(f => f.ExamPackageId);

        builder.HasOne<ExamPackage>().WithMany().HasForeignKey(f => f.ExamPackageId);
    }
}
