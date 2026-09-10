namespace Web.Api.Common.Crypto;

// The two halves of a sealed package: the ciphertext that becomes package.bin, and the header that
// becomes package.hdr. Neither the content key nor the one-time code appears here - by the time
// Seal returns, both have been wiped from memory.
public sealed record SealedPackage(byte[] Ciphertext, PackageHeader Header);
