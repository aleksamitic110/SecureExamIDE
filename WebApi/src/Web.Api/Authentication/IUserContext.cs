namespace Web.Api.Authentication;

public interface IUserContext
{
    Guid UserId { get; }

    // The device credential the caller authenticated with, or null when they used an ordinary
    // password login. Submitting requires a device token, so this is what tells the two apart -
    // and it records which bound machine a solution actually came from.
    Guid? DeviceId { get; }
}
