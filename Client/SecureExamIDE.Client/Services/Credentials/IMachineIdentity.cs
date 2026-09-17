namespace SecureExamIDE.Client.Services.Credentials;

// A stable identifier of this computer, used to bind locally stored secrets to the machine.
public interface IMachineIdentity
{
    string GetMachineId();
}
