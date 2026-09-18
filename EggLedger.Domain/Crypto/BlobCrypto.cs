using System.Security.Cryptography;

namespace EggLedger.Domain.Crypto;

public static class BlobCrypto {
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    public static string Encrypt(string hexKey, byte[] plaintext) {
        ArgumentNullException.ThrowIfNull(plaintext);
        var key = DecodeKey(hexKey);

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using (var gcm = new AesGcm(key, TagSize)) {
            gcm.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        var sealed_ = new byte[NonceSize + ciphertext.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, sealed_, 0, NonceSize);
        Buffer.BlockCopy(ciphertext, 0, sealed_, NonceSize, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, sealed_, NonceSize + ciphertext.Length, TagSize);
        return Convert.ToBase64String(sealed_);
    }

    public static byte[] Decrypt(string hexKey, string b64) {
        ArgumentNullException.ThrowIfNull(b64);
        var key = DecodeKey(hexKey);

        var data = Convert.FromBase64String(b64);
        if (data.Length < NonceSize + TagSize) {
            throw new CryptographicException("blob too short");
        }

        var nonce = new byte[NonceSize];
        Buffer.BlockCopy(data, 0, nonce, 0, NonceSize);

        var bodyLen = data.Length - NonceSize - TagSize;
        var ciphertext = new byte[bodyLen];
        Buffer.BlockCopy(data, NonceSize, ciphertext, 0, bodyLen);

        var tag = new byte[TagSize];
        Buffer.BlockCopy(data, NonceSize + bodyLen, tag, 0, TagSize);

        var plaintext = new byte[bodyLen];
        using (var gcm = new AesGcm(key, TagSize)) {
            gcm.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        return plaintext;
    }

    private static byte[] DecodeKey(string hexKey) {
        ArgumentNullException.ThrowIfNull(hexKey);
        var key = Convert.FromHexString(hexKey);
        if (key.Length != KeySize) {
            throw new CryptographicException(
                $"AES-256 key must be {KeySize} bytes ({KeySize * 2} hex chars), got {key.Length}");
        }
        return key;
    }
}
