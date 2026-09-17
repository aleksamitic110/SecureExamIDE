namespace SecureExamIDE.Client.Services.Unlock;

// package.hdr, as the server writes it when it seals a sitting. It carries everything needed to
// unlock the package except the one-time code: the KDF parameters, the salt, and the content key
// wrapped under the key the code derives.
public sealed record PackageHeader(
    int Version,
    string Algorithm,
    string Kdf,
    int KdfIterations,
    int KdfMemoryKib,
    int KdfParallelism,
    string CodeNormalization,
    string KdfSalt,
    string WrappedKey,
    string WrappedKeyNonce,
    string WrappedKeyTag,
    string PackageNonce,
    string PackageTag);
