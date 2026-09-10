using Microsoft.EntityFrameworkCore;
using Web.Api.Authorization;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Database;
using Web.Api.Features.Users;

namespace Web.Api.Features.Exams;

// The student-facing catalog, and the first read in the system that is not scoped to its owner.
// It shows published exams only - a draft is the professor's private workspace, and GetExam is
// where they look at it.
public static class GetPublishedExams
{
    public sealed record Query(int Page, int PageSize, string? Subject) : IQuery<PagedList<Response>>;

    public sealed record Response
    {
        public Guid Id { get; init; }

        public string Title { get; init; }

        public string Description { get; init; }

        public string Subject { get; init; }

        public DateTime? PublishedAt { get; init; }

        public string ProfessorFirstName { get; init; }

        public string ProfessorLastName { get; init; }

        public int FileCount { get; init; }

        public int DependencyCount { get; init; }

        // What the student is about to pull over a home connection, so it is worth showing before
        // they start the download rather than after.
        public long TotalSizeBytes { get; init; }
    }

    internal sealed class Handler(ApplicationDbContext context)
        : IQueryHandler<Query, PagedList<Response>>
    {
        public async Task<Result<PagedList<Response>>> Handle(Query query, CancellationToken cancellationToken)
        {
            IQueryable<ExamPackage> exams = context.ExamPackages
                .AsNoTracking()
                .Where(e => e.Status == ExamPackageStatus.Published);

            // An unparseable subject matches nothing rather than being ignored, so a filtered
            // request never quietly returns the unfiltered catalog.
            if (!string.IsNullOrWhiteSpace(query.Subject))
            {
                Result<ExamSubject> subjectResult = ExamSubject.Create(query.Subject);

                if (subjectResult.IsFailure)
                {
                    return EmptyPage(query);
                }

                ExamSubject subject = subjectResult.Value;

                // Compared value object to value object: reaching into .Value here would not
                // translate to SQL.
                exams = exams.Where(e => e.Subject == subject);
            }

            // Id is a tie-break, not decoration: without a total order two exams published in the
            // same instant could swap places between requests, and a row would then be repeated on
            // one page and missing from another.
            IQueryable<Response> catalog =
                from exam in exams
                join professor in context.Users on exam.OwnerProfessorId equals professor.Id
                orderby exam.PublishedAt descending, exam.Id
                select new Response
                {
                    Id = exam.Id,
                    Title = exam.Title.Value,
                    Description = exam.Description.Value,
                    Subject = exam.Subject.Value,
                    PublishedAt = exam.PublishedAt,
                    ProfessorFirstName = professor.FirstName.Value,
                    ProfessorLastName = professor.LastName.Value,
                    FileCount = context.ExamFiles.Count(f => f.ExamPackageId == exam.Id),
                    DependencyCount = context.ExamDependencies.Count(d => d.ExamPackageId == exam.Id),
                    TotalSizeBytes =
                        (context.ExamFiles
                            .Where(f => f.ExamPackageId == exam.Id)
                            .Sum(f => (long?)f.SizeBytes) ?? 0)
                        + (context.ExamDependencies
                            .Where(d => d.ExamPackageId == exam.Id)
                            .Sum(d => (long?)d.SizeBytes) ?? 0)
                };

            // Still an IQueryable: the page is cut by the database, not after the rows arrive.
            return await PagedList<Response>.CreateAsync(
                catalog,
                query.Page,
                query.PageSize,
                cancellationToken);
        }

        private static PagedList<Response> EmptyPage(Query query) => new()
        {
            Items = [],
            Page = PagedList<Response>.NormalizePage(query.Page),
            PageSize = PagedList<Response>.NormalizePageSize(query.PageSize),
            TotalCount = 0
        };
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("exams", async (
                int? page,
                int? pageSize,
                string? subject,
                IQueryHandler<Query, PagedList<Response>> handler,
                CancellationToken cancellationToken) =>
            {
                var query = new Query(
                    page ?? 1,
                    pageSize ?? PagedList<Response>.DefaultPageSize,
                    subject);

                Result<PagedList<Response>> result = await handler.Handle(query, cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .HasPermission(Permissions.ExamsRead)
            .WithTags(Tags.Exams);
        }
    }
}
