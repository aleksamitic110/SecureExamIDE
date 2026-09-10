namespace Web.Api.Authentication;

public interface IDeviceCredentialProvider
{
    // Produces a fresh device secret. Returned to the client once and never stored.
    string Generate();

    // Derives the value persisted in device_credentials.secret_hash.
    string Hash(string secret);
}
