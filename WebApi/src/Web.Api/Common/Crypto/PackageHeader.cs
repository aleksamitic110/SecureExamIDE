namespace Web.Api.Common.Crypto;

// Everything a client needs to unlock a package, and nothing that would let it do so without the
// one-time code. This ships inside the download, which is what makes unlocking work with no
// network at all on exam day - the whole point of the system.
//
// The salt and the wrapped key are safe to publish: without the code, deriving the key-encryption
// key from them is exactly the offline attack Argon2id is chosen to make expensive.
public sealed record PackageHeader
{
    public int Version { get; init; } = CurrentVersion;

    public string Algorithm { get; init; } = "AES-256-GCM";

    public string Kdf { get; init; } = "Argon2id";

    public int KdfIterations { get; init; }

    public int KdfMemoryKib { get; init; }

    public int KdfParallelism { get; init; }

    // How the typed code is folded before it reaches the KDF. Recorded rather than assumed,
    // because the client has to normalise identically or the derived key will not match.
    public string CodeNormalization { get; init; } = "uppercase; O->0, I->1, L->1; drop anything outside 0-9 A-Z";

    public string KdfSalt { get; init; } = string.Empty;

    public string WrappedKey { get; init; } = string.Empty;

    public string WrappedKeyNonce { get; init; } = string.Empty;

    public string WrappedKeyTag { get; init; } = string.Empty;

    public string PackageNonce { get; init; } = string.Empty;

    public string PackageTag { get; init; } = string.Empty;

    public const int CurrentVersion = 1;
}
