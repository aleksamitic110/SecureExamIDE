using Web.Api.Common.ValueObjects;
using Web.Api.Features.Exams;

namespace Web.Api.UnitTests.Abstractions;

// Terse construction of value objects in test arrangements. Each one throws if the literal in a
// test is not actually valid, which is the behaviour we want: a test that seeds a malformed value
// should fail loudly rather than quietly exercise an impossible state.
internal static class ValueObjectTestExtensions
{
    public static Email AsEmail(this string value) => Email.Create(value).Value;

    public static PersonName AsPersonName(this string value) => PersonName.Create(value).Value;

    public static IndexNumber AsIndexNumber(this string value) => IndexNumber.Create(value).Value;

    public static DeviceName AsDeviceName(this string value) => DeviceName.Create(value).Value;

    public static FileName AsFileName(this string value) => FileName.Create(value).Value;

    public static ContentType AsContentType(this string value) => ContentType.Create(value).Value;

    public static ObjectKey AsObjectKey(this string value) => ObjectKey.Create(value).Value;

    public static Sha256Hash AsSha256(this string value) => Sha256Hash.Create(value).Value;

    public static ExamTitle AsExamTitle(this string value) => ExamTitle.Create(value).Value;

    public static ExamDescription AsExamDescription(this string value) => ExamDescription.Create(value).Value;

    public static ExamSubject AsExamSubject(this string value) => ExamSubject.Create(value).Value;

    public static DependencyName AsDependencyName(this string value) => DependencyName.Create(value).Value;

    public static DependencyVersion AsDependencyVersion(this string value) =>
        DependencyVersion.Create(value).Value;
}
