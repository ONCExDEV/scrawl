using System.Security.Cryptography;
using System.Text;
using Scrawl.Models;

namespace Scrawl.Services;

/// <summary>
/// Password protection for notes. A locked note is stored encrypted (AES-256-GCM, key derived with PBKDF2),
/// so its text is not readable in notes.json. The derived key only lives in memory while the note is unlocked.
/// </summary>
public static class NoteLock
{
    private const int Iterations = 200_000;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static byte[] DeriveKey(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);

    public static string Encrypt(string text, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = Encoding.UTF8.GetBytes(text);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);
        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    private static bool TryDecrypt(string vault, byte[] key, out string text)
    {
        text = string.Empty;
        try
        {
            var data = Convert.FromBase64String(vault);
            var nonce = data[..NonceSize];
            var tag = data[NonceSize..(NonceSize + TagSize)];
            var cipher = data[(NonceSize + TagSize)..];
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);
            text = Encoding.UTF8.GetString(plain);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Protects a note with a password. The note ends up locked (text cleared from memory).</summary>
    public static void Lock(Note note, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        note.LockSalt = Convert.ToBase64String(salt);
        note.Key = DeriveKey(password, salt);
        note.Vault = Encrypt(note.Content, note.Key);
        Relock(note);
    }

    /// <summary>Seals the current text and forgets the key, so the note asks for the password again.</summary>
    public static void Relock(Note note)
    {
        if (note.Key is not null) note.Vault = Encrypt(note.Content, note.Key);
        note.Key = null;
        note.Content = string.Empty;
    }

    /// <summary>Unlocks a note for this session. Returns false for a wrong password.</summary>
    public static bool TryUnlock(Note note, string password)
    {
        if (note.LockSalt is null || note.Vault is null) return true;
        var key = DeriveKey(password, Convert.FromBase64String(note.LockSalt));
        if (!TryDecrypt(note.Vault, key, out var text)) return false;
        note.Key = key;
        note.Content = text;
        return true;
    }

    /// <summary>Checks a password without changing the note.</summary>
    public static bool Verify(Note note, string password)
    {
        if (note.LockSalt is null || note.Vault is null) return true;
        return TryDecrypt(note.Vault, DeriveKey(password, Convert.FromBase64String(note.LockSalt)), out _);
    }

    /// <summary>Removes the password. The note must be unlocked first.</summary>
    public static void Remove(Note note)
    {
        note.LockSalt = null;
        note.Vault = null;
        note.Key = null;
        note.PinLength = 0;
    }
}
