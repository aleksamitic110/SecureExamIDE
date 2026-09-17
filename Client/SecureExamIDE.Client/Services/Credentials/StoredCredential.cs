namespace SecureExamIDE.Client.Services.Credentials;

// What this computer keeps once it is bound to an account. DeviceCredential is the secret the API
// returned exactly once, at registration or login; the e-mail is kept so an account whose address
// is not verified yet can be sent straight to the code screen after a restart.
public sealed record StoredCredential(string Email, Guid DeviceId, string DeviceCredential);
