using System.Runtime.Versioning;
using Microsoft.Win32;

namespace SecureExamIDE.Client.Services.Credentials;

// Windows: the MachineGuid Windows writes at installation. Linux: the systemd machine id. Both
// survive reboots and differ between computers; the host name is only a last resort.
internal sealed class MachineIdentity : IMachineIdentity
{
    public string GetMachineId()
    {
        if (OperatingSystem.IsWindows())
        {
            return ReadWindowsMachineGuid() ?? Environment.MachineName;
        }

        foreach (string path in LinuxMachineIdFiles)
        {
            if (File.Exists(path))
            {
                string id = File.ReadAllText(path).Trim();

                if (id.Length > 0)
                {
                    return id;
                }
            }
        }

        return Environment.MachineName;
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadWindowsMachineGuid()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");

        return key?.GetValue("MachineGuid") as string;
    }

    private static readonly string[] LinuxMachineIdFiles = ["/etc/machine-id", "/var/lib/dbus/machine-id"];
}
