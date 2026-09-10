namespace Web.Api.Common.Crypto;

public interface ICryptoService
{
    // Encrypts the payload under a fresh random content key, then wraps that key with a key
    // derived from the one-time code. There is deliberately no Open counterpart: unsealing is the
    // client's job, offline, and the server is never able to do it.
    SealedPackage Seal(byte[] plaintext, string oneTimeCode);
}
