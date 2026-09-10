namespace Web.Api.Authorization;

// The complete catalog of permissions in the system. Permissions are never stored in the
// database - PermissionProvider derives them from the user's role in code.
public static class Permissions
{
    // Read one's own account details.
    public const string UsersRead = "users:read";

    // List and revoke the credentials bound to one's own machines.
    public const string DevicesManage = "devices:manage";

    // Browse the catalog of published exams and download their packages.
    public const string ExamsRead = "exams:read";

    // Create, upload to, publish and archive exams. Professors only.
    public const string ExamsManage = "exams:manage";

    // Submit an encrypted solution and its activity logs. Students only.
    public const string SubmissionsSubmit = "submissions:submit";

    // Read the submissions and logs of an owned exam. Professors only.
    public const string SubmissionsReview = "submissions:review";
}
