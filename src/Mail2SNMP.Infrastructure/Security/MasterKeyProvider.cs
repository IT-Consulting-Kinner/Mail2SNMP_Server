using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Infrastructure.Security;

/// <summary>
/// Generates or loads the AES-256 master key from a file, with restricted ACL permissions.
/// </summary>
public static class MasterKeyProvider
{
    private const int KeySizeBytes = 32;

    /// <summary>
    /// Loads the master key from the specified path, or generates a new 256-bit key and writes it to disk.
    /// </summary>
    public static byte[] LoadOrCreate(string keyFilePath, ILogger logger)
    {
        if (File.Exists(keyFilePath))
        {
            logger.LogInformation("Loading master key from {Path}", keyFilePath);
            return File.ReadAllBytes(keyFilePath);
        }

        logger.LogInformation("Generating new master key at {Path}", keyFilePath);
        var key = new byte[KeySizeBytes];
        RandomNumberGenerator.Fill(key);

        var dir = Path.GetDirectoryName(keyFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllBytes(keyFilePath, key);

        // Set restrictive file permissions
        SetRestrictivePermissions(keyFilePath, logger);

        return key;
    }

    /// <summary>
    /// Sets restrictive ACL on the master key file so only SYSTEM and Administrators can access it.
    /// On Linux, sets chmod 600 (owner-only read/write).
    /// </summary>
    private static void SetRestrictivePermissions(string filePath, ILogger logger)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                SetWindowsPermissions(filePath, logger);
            }
            else
            {
                // Linux/macOS: chmod 600
                File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                logger.LogInformation("Master key file permissions set to 600 (owner-only)");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to set restrictive permissions on master key file. " +
                "Please manually restrict access to: {Path}", filePath);
        }
    }

    private static void SetWindowsPermissions(string filePath, ILogger logger)
    {
        if (!OperatingSystem.IsWindows()) return;

        var fileInfo = new FileInfo(filePath);
        var security = fileInfo.GetAccessControl();

        // Remove inherited permissions
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        // Remove all existing access rules
        var existingRules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in existingRules)
        {
            security.RemoveAccessRule(rule);
        }

        // Grant full control to SYSTEM
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));

        // Grant full control to Administrators
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));

        fileInfo.SetAccessControl(security);
        logger.LogInformation("Master key file ACL set: SYSTEM + Administrators only");
    }

    /// <summary>
    /// Returns the platform-specific default file path for the master key.
    /// </summary>
    public static string GetDefaultKeyPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(programData, "IT-Consulting Kinner", "Mail2SNMP_Server", "Key", "master.key");
        }
        return "/etc/mail2snmp/keys/master.key";
    }
}
