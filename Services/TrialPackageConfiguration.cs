using System.Security.Cryptography;
using System.Text;
using System.IO;

namespace TensileNeW.Services;

public static class TrialPackageConfiguration
{
    public const string FileName = "package.config";

    private static readonly byte[] EncryptionKey = SHA256.HashData(
        Encoding.UTF8.GetBytes("ECS package configuration v1"));

    private static readonly byte[] InitializationVector = SHA256.HashData(
        Encoding.UTF8.GetBytes("ECS package configuration IV v1"))[..16];

    public static void EnsureTrialConfiguration(string directoryPath)
    {
        string filePath = Path.Combine(directoryPath, FileName);
        if (!File.Exists(filePath))
        {
            Write(filePath, isTrial: true);
        }
    }

    public static void Write(string filePath, bool isTrial)
    {
        using Aes aes = Aes.Create();
        aes.Key = EncryptionKey;
        aes.IV = InitializationVector;

        using FileStream output = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using CryptoStream cryptoStream = new(output, aes.CreateEncryptor(), CryptoStreamMode.Write);
        cryptoStream.WriteByte(1);
        cryptoStream.WriteByte(isTrial ? (byte)1 : (byte)0);
    }
}
