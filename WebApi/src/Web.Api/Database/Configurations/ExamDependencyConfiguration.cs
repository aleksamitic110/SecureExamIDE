using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Web.Api.Common.ValueObjects;
using Web.Api.Features.Exams;

namespace Web.Api.Database.Configurations;

internal sealed class ExamDependencyConfiguration : IEntityTypeConfiguration<ExamDependency>
{
    public void Configure(EntityTypeBuilder<ExamDependency> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Name)
            .HasConversion(name => name.Value, value => DependencyName.FromTrusted(value))
            .HasMaxLength(DependencyName.MaxLength);

        builder.Property(d => d.Version)
            .HasConversion(version => version.Value, value => DependencyVersion.FromTrusted(value))
            .HasMaxLength(DependencyVersion.MaxLength);

        builder.Property(d => d.Platform)
            .HasConversion<string>()
            .HasMaxLength(PlatformMaxLength);

        builder.Property(d => d.ContentType)
            .HasConversion(contentType => contentType.Value, value => ContentType.FromTrusted(value))
            .HasMaxLength(ContentType.MaxLength);

        builder.Property(d => d.ObjectKey)
            .HasConversion(objectKey => objectKey.Value, value => ObjectKey.FromTrusted(value))
            .HasMaxLength(ObjectKey.MaxLength);

        // One stored object backs at most one row, so committing the same upload twice is rejected
        // by the database itself rather than only by the handler's check.
        builder.HasIndex(d => d.ObjectKey).IsUnique();

        // Name, version and platform identify a dependency within its exam, so the same build of a
        // tool cannot be attached twice under two different object keys, while one tool can still
        // come as a Windows and a Linux archive.
        builder.HasIndex(d => new { d.ExamPackageId, d.Name, d.Version, d.Platform }).IsUnique();

        builder.HasOne<ExamPackage>().WithMany().HasForeignKey(d => d.ExamPackageId);
    }

    private const int PlatformMaxLength = 20;
}
