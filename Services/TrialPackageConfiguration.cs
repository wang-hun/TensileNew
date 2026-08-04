using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Diagnostics;

namespace TensileNeW.Services;

public static class TrialPackageConfiguration
{
    public const string FileName = "package.config";

    private static readonly byte[] EncryptionKey = SHA256.HashData(
        Encoding.UTF8.GetBytes("ECS package configuration v1"));

    private static readonly byte[] InitializationVector = SHA256.HashData(
        Encoding.UTF8.GetBytes("ECS package configuration IV v1"))[..16];

    /*
     * ========================================================================
     * 仅在程序启动阶段读取试用状态。
     * 调用方将结果保存到全局 RAM 配置中。
     * ========================================================================
     */
    public static bool ReadStartupTrialState(string directoryPath)
    {
        if (Debugger.IsAttached)
        {
            // 调试器附加时不访问外置文件，仍按当前编译配置确定版本类型。
#if DEBUG
            return false;
#else
            return true;
#endif
        }

        string filePath = Path.Combine(directoryPath, FileName);
        if (!File.Exists(filePath))
        {
            Write(filePath, isTrial: true);
        }

        return Read(filePath);
    }

    /*
     * ========================================================================
     * 启动阶段试用状态读取结束。
     * ========================================================================
     */

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

    private static bool Read(string filePath)
    {
        using Aes aes = Aes.Create();
        aes.Key = EncryptionKey;
        aes.IV = InitializationVector;

        using FileStream input = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using CryptoStream cryptoStream = new(input, aes.CreateDecryptor(), CryptoStreamMode.Read);

        if (cryptoStream.ReadByte() != 1)
        {
            throw new InvalidDataException("试用配置文件无效。");
        }

        int trialFlag = cryptoStream.ReadByte();
        if (trialFlag is not 0 and not 1)
        {
            throw new InvalidDataException("试用配置文件无效。");
        }

        return trialFlag == 1;
    }
}
