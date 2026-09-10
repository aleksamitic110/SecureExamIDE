namespace Web.Api.Authentication;

// Settings that govern who may create an account. Bound from the "Registration" configuration
// section.
public sealed class RegistrationOptions
{
    // Shared secret a caller must present to register as a professor. Students register freely;
    // professor accounts control exam content, so self-registration into that role is gated.
    // An empty value disables professor registration entirely.
    public string ProfessorRegistrationCode { get; set; } = string.Empty;
}
