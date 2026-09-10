namespace Web.Api.Common.Crypto;

public interface IOneTimeCodeProvider
{
    // Produces the code the professor reads out on exam day. Returned to them once and never
    // stored in a recoverable form.
    string Generate();

    // The digest kept in the database, so a submitted code can be recognised without the server
    // ever being able to unlock a package itself.
    string Hash(string oneTimeCode);
}
