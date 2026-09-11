namespace Web.Api.Features.Users;

// Distinguishes the two kinds of account the system knows about. Persisted as text so the
// column stays readable and so reordering the members can never silently repurpose a row.
public enum Role
{
    Student = 1,
    Professor = 2
}
