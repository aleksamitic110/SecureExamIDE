using Web.Api.Common.ValueObjects;
using Web.Api.Database;
using Web.Api.Features.Devices;
using Web.Api.Features.ExamSessions;
using Web.Api.Features.Exams;
using Web.Api.Features.Submissions;
using Web.Api.Features.Users;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Submissions;

// Builds the rows a professor's review reads: an exam owned by a professor, one sitting of it, and
// students who handed in work from a bound machine. Shared by the review handler tests because
// every one of them needs the same four linked rows.
internal static class ReviewSeed
{
    public static async Task<Guid> SeedSessionAsync(
        ApplicationDbContext context,
        Guid ownerProfessorId,
        DateTime startsAt,
        DateTime endsAt)
    {
        var exam = new ExamPackage
        {
            Id = Guid.NewGuid(),
            Title = "Compilers".AsExamTitle(),
            Description = "Final".AsExamDescription(),
            Subject = "Compiler Construction".AsExamSubject(),
            OwnerProfessorId = ownerProfessorId,
            Status = ExamPackageStatus.Published,
            CreatedAt = startsAt.AddDays(-10),
            PublishedAt = startsAt.AddDays(-9)
        };

        var session = new ExamSession
        {
            Id = Guid.NewGuid(),
            ExamPackageId = exam.Id,
            CreatedByProfessorId = ownerProfessorId,
            StartsAt = startsAt,
            EndsAt = endsAt,
            IsActive = true,
            OneTimeCodeHash = SolutionDigest.AsSha256(),
            PackageObjectKey = $"exams/{exam.Id}/sessions/x/package.bin".AsObjectKey(),
            HeaderObjectKey = $"exams/{exam.Id}/sessions/x/package.hdr".AsObjectKey(),
            PackageSizeBytes = 100,
            PackageSha256 = SolutionDigest.AsSha256(),
            CreatedAt = startsAt.AddDays(-5)
        };

        context.ExamPackages.Add(exam);
        context.ExamSessions.Add(session);
        await context.SaveChangesAsync();

        return session.Id;
    }

    public static async Task<Submission> SeedSubmissionAsync(
        ApplicationDbContext context,
        Guid sessionId,
        DateTime submittedAt,
        string firstName = "Ana",
        string lastName = "Anic",
        string indexNumber = "12345",
        string deviceName = "ana-laptop")
    {
        var student = new User
        {
            Id = Guid.NewGuid(),
            Email = $"student.{Guid.NewGuid():N}@example.com".AsEmail(),
            FirstName = firstName.AsPersonName(),
            LastName = lastName.AsPersonName(),
            PasswordHash = "not-a-real-hash",
            Role = Role.Student,
            IndexNumber = indexNumber.AsIndexNumber(),
            IsActive = true
        };

        var device = new DeviceCredential
        {
            Id = Guid.NewGuid(),
            UserId = student.Id,
            DeviceName = deviceName.AsDeviceName(),
            SecretHash = "not-a-real-hash",
            CreatedAt = submittedAt.AddDays(-30)
        };

        var submission = new Submission
        {
            Id = Guid.NewGuid(),
            ExamSessionId = sessionId,
            StudentId = student.Id,
            DeviceCredentialId = device.Id,
            SolutionObjectKey = SubmissionObjectKeys.NewSolutionKey(sessionId, student.Id),
            SolutionSizeBytes = 25,
            SolutionSha256 = SolutionDigest.AsSha256(),
            ActivityLogObjectKey = SubmissionObjectKeys.NewActivityLogKey(sessionId, student.Id),
            ActivityLogSizeBytes = 69,
            ActivityLogSha256 = ActivityLogDigest.AsSha256(),
            SubmittedAt = submittedAt
        };

        context.Users.Add(student);
        context.DeviceCredentials.Add(device);
        context.Submissions.Add(submission);
        await context.SaveChangesAsync();

        return submission;
    }

    public const string SolutionDigest = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    public static readonly string ActivityLogDigest = new('a', 64);
}
