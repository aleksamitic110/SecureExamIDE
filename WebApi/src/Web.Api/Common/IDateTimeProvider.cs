namespace Web.Api.Common;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
