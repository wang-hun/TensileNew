using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Diagnostics;

namespace TensileNeW.Services;

public static class TrialPackageConfiguration
{
    public const string FileName = "package.config";
    public const int TrialDataSaveLimit = 50;
    private const string ApplicationDataDirectoryName = "ECS";
    private const byte ConfigurationVersion = 2;

    private static readonly byte[] EncryptionKey = SHA256.HashData(
        Encoding.UTF8.GetBytes("ECS package configuration v1"));

    private static readonly byte[] InitializationVector = SHA256.HashData(
        Encoding.UTF8.GetBytes("ECS package configuration IV v1"))[..16];

    public static TrialPackageState ReadStartupTrialState(string directoryPath)
    {
        if (Debugger.IsAttached)
        {
#if DEBUG
            return TrialPackageState.Full;
#else
            return TrialPackageState.Trial;
#endif
        }

        string runtimeFilePath = Path.Combine(directoryPath, FileName);

        if (File.Exists(runtimeFilePath))
        {
            TrialPackageState runtimeFullState = Read(runtimeFilePath);
            if (runtimeFullState.SkipPermissionFileSynchronization)
            {
                return runtimeFullState;
            }

            if (!runtimeFullState.IsTrial)
            {
                string managedFullFilePath = GetManagedFilePath();
                if (!TryRead(managedFullFilePath, out TrialPackageState managedFullState) || managedFullState.IsTrial)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(managedFullFilePath)!);
                    File.Copy(runtimeFilePath, managedFullFilePath, overwrite: true);
                    return runtimeFullState;
                }

                if (!FilesAreEqual(runtimeFilePath, managedFullFilePath))
                {
                    File.Copy(managedFullFilePath, runtimeFilePath, overwrite: true);
                }

                return managedFullState;
            }
        }

        string managedFilePath = GetManagedFilePath();

        if (File.Exists(managedFilePath))
        {
            TrialPackageState managedState = Read(managedFilePath);
            if (!File.Exists(runtimeFilePath) || !FilesAreEqual(runtimeFilePath, managedFilePath))
            {
                File.Copy(managedFilePath, runtimeFilePath, overwrite: true);
            }

            return managedState;
        }

        if (!File.Exists(runtimeFilePath))
        {
            Write(runtimeFilePath, TrialPackageState.Trial);
        }

        TrialPackageState runtimeState = Read(runtimeFilePath);
        if (runtimeState.IsTrial)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(managedFilePath)!);
            File.Copy(runtimeFilePath, managedFilePath, overwrite: false);
        }

        return runtimeState;
    }

    public static void Write(string filePath, bool isTrial)
    {
        Write(filePath, isTrial ? TrialPackageState.Trial : TrialPackageState.Full);
    }

    public static void Write(string filePath, TrialPackageState state)
    {
        using Aes aes = Aes.Create();
        aes.Key = EncryptionKey;
        aes.IV = InitializationVector;

        using FileStream output = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using CryptoStream cryptoStream = new(output, aes.CreateEncryptor(), CryptoStreamMode.Write);
        using BinaryWriter writer = new(cryptoStream, Encoding.UTF8, leaveOpen: true);
        writer.Write(ConfigurationVersion);
        writer.Write(state.IsTrial);
        writer.Write(state.StartupCount);
        writer.Write(state.DataSaveCount);
        writer.Write(state.SkipPermissionFileSynchronization);
    }

    public static TrialPackageState UpdateTrialCounts(
        string directoryPath,
        TrialPackageState currentState,
        bool incrementStartupCount,
        bool incrementDataSaveCount)
    {
        if (!currentState.IsTrial)
        {
            return currentState;
        }

        if (incrementDataSaveCount && currentState.DataSaveCount >= TrialDataSaveLimit)
        {
            return currentState;
        }

        TrialPackageState updatedState = new(
            IsTrial: true,
            StartupCount: incrementStartupCount
                ? checked(currentState.StartupCount + 1)
                : currentState.StartupCount,
            DataSaveCount: incrementDataSaveCount
                ? checked(currentState.DataSaveCount + 1)
                : currentState.DataSaveCount);

        string managedFilePath = GetManagedFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(managedFilePath)!);
        Write(managedFilePath, updatedState);

        string runtimeFilePath = Path.Combine(directoryPath, FileName);
        if (!string.Equals(runtimeFilePath, managedFilePath, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(managedFilePath, runtimeFilePath, overwrite: true);
        }

        return updatedState;
    }

    private static TrialPackageState Read(string filePath)
    {
        using Aes aes = Aes.Create();
        aes.Key = EncryptionKey;
        aes.IV = InitializationVector;

        using FileStream input = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using CryptoStream cryptoStream = new(input, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using BinaryReader reader = new(cryptoStream, Encoding.UTF8, leaveOpen: true);

        if (reader.ReadByte() != ConfigurationVersion)
        {
            throw new InvalidDataException("试用配置文件无效。");
        }

        byte trialFlag = reader.ReadByte();
        if (trialFlag is not 0 and not 1)
        {
            throw new InvalidDataException("试用配置文件无效。");
        }

        bool isTrial = trialFlag == 1;
        int startupCount = reader.ReadInt32();
        int dataSaveCount = reader.ReadInt32();
        if (startupCount < 0 || dataSaveCount < 0)
        {
            throw new InvalidDataException("试用配置文件无效。");
        }

        bool skipPermissionFileSynchronization = false;
        try
        {
            skipPermissionFileSynchronization = reader.ReadBoolean();
        }
        catch (EndOfStreamException)
        {
            // Existing files have no trailing synchronization marker.
        }

        return new TrialPackageState(isTrial, startupCount, dataSaveCount, skipPermissionFileSynchronization);
    }

    private static bool TryRead(string filePath, out TrialPackageState state)
    {
        state = TrialPackageState.Full;
        if (!File.Exists(filePath))
        {
            return false;
        }

        try
        {
            state = Read(filePath);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    public static string GetManagedFilePath()
    {
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, ApplicationDataDirectoryName, FileName);
    }

    private static bool FilesAreEqual(string firstPath, string secondPath)
    {
        FileInfo firstFile = new(firstPath);
        FileInfo secondFile = new(secondPath);
        if (firstFile.Length != secondFile.Length)
        {
            return false;
        }

        const int bufferSize = 81920;
        byte[] firstBuffer = new byte[bufferSize];
        byte[] secondBuffer = new byte[bufferSize];
        using FileStream firstStream = new(firstPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using FileStream secondStream = new(secondPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        while (true)
        {
            int firstRead = firstStream.Read(firstBuffer, 0, firstBuffer.Length);
            int secondRead = secondStream.Read(secondBuffer, 0, secondBuffer.Length);
            if (firstRead != secondRead)
            {
                return false;
            }

            if (firstRead == 0)
            {
                return true;
            }

            if (!firstBuffer.AsSpan(0, firstRead).SequenceEqual(secondBuffer.AsSpan(0, secondRead)))
            {
                return false;
            }
        }
    }
}

public sealed record TrialPackageState(bool IsTrial, int StartupCount, int DataSaveCount, bool SkipPermissionFileSynchronization = false)
{
    public static TrialPackageState Trial { get; } = new(true, StartupCount: 0, DataSaveCount: 0);
    public static TrialPackageState Full { get; } = new(false, StartupCount: 0, DataSaveCount: 0);
    public static TrialPackageState FullWithoutPermissionFileSynchronization { get; } = new(
        false,
        StartupCount: 0,
        DataSaveCount: 0,
        SkipPermissionFileSynchronization: true);
}
