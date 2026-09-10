using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Web.Api.Features.Exams;
using Web.Api.Features.Users;

namespace Web.Api.Database.Configurations;

internal sealed class ExamPackageConfiguration : IEntityTypeConfiguration<ExamPackage>
{
    public void Configure(EntityTypeBuilder<ExamPackage> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Title)
            .HasConversion(title => title.Value, value => ExamTitle.FromTrusted(value))
            .HasMaxLength(ExamTitle.MaxLength);

        builder.Property(e => e.Description)
            .HasConversion(description => description.Value, value => ExamDescription.FromTrusted(value))
            .HasMaxLength(ExamDescription.MaxLength);

        builder.Property(e => e.Subject)
            .HasConversion(subject => subject.Value, value => ExamSubject.FromTrusted(value))
            .HasMaxLength(ExamSubject.MaxLength);

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(StatusMaxLength);

        builder.HasIndex(e => e.OwnerProfessorId);

        builder.HasOne<User>().WithMany().HasForeignKey(e => e.OwnerProfessorId);
    }

    private const int StatusMaxLength = 20;
}
