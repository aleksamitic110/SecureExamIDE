using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Web.Api.Common.ValueObjects;
using Web.Api.Features.Devices;
using Web.Api.Features.ExamSessions;
using Web.Api.Features.Submissions;
using Web.Api.Features.Users;

namespace Web.Api.Database.Configurations;

internal sealed class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.SolutionObjectKey)
            .HasConversion(objectKey => objectKey.Value, value => ObjectKey.FromTrusted(value))
            .HasMaxLength(ObjectKey.MaxLength);

        builder.Property(s => s.SolutionSha256)
            .HasConversion(sha256 => sha256.Value, value => Sha256Hash.FromTrusted(value))
            .HasMaxLength(Sha256Hash.Length)
            .IsFixedLength();

        builder.Property(s => s.ActivityLogObjectKey)
            .HasConversion(objectKey => objectKey.Value, value => ObjectKey.FromTrusted(value))
            .HasMaxLength(ObjectKey.MaxLength);

        builder.Property(s => s.ActivityLogSha256)
            .HasConversion(sha256 => sha256.Value, value => Sha256Hash.FromTrusted(value))
            .HasMaxLength(Sha256Hash.Length)
            .IsFixedLength();

        builder.HasIndex(s => s.SolutionObjectKey).IsUnique();
        builder.HasIndex(s => s.ActivityLogObjectKey).IsUnique();

        // One submission per student per sitting, enforced by the database rather than only by the
        // handler's check. "The student cannot alter it afterwards" is the property the exam mode
        // exists to provide, so a second row must be impossible even under a race.
        builder.HasIndex(s => new { s.ExamSessionId, s.StudentId }).IsUnique();

        builder.HasOne<ExamSession>().WithMany().HasForeignKey(s => s.ExamSessionId);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.StudentId);

        // Restricted rather than cascading: a submission is the record of what a student handed in,
        // and it must not disappear because the machine it came from was removed.
        builder.HasOne<DeviceCredential>()
            .WithMany()
            .HasForeignKey(s => s.DeviceCredentialId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
