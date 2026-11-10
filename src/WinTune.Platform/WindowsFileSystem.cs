using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using WinTune.Platform.Abstractions;

namespace WinTune.Platform;

/// <summary>Implementação de <see cref="IFileSystem"/> sobre o sistema de arquivos real.</summary>
public sealed class WindowsFileSystem : IFileSystem
{
    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public FileEntry? GetFile(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? ToEntry(info) : null;
    }

    /// <inheritdoc />
    public IEnumerable<FileEntry> EnumerateFiles(string directory, string searchPattern, bool recursive)
    {
        var pending = new Stack<string>();
        pending.Push(directory);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false,
        };

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            DirectoryInfo dir;
            try
            {
                dir = new DirectoryInfo(current);
                if (!dir.Exists)
                {
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                continue;
            }

            List<FileInfo> files;
            try
            {
                files = dir.EnumerateFiles(searchPattern, options).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                files = [];
            }

            foreach (var f in files)
            {
                yield return ToEntry(f);
            }

            if (!recursive)
            {
                continue;
            }

            List<DirectoryInfo> subdirs;
            try
            {
                subdirs = dir.EnumerateDirectories("*", options).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                subdirs = [];
            }

            foreach (var sub in subdirs)
            {
                // Nunca percorre links simbólicos/junções.
                if (!sub.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    pending.Push(sub.FullName);
                }
            }
        }
    }

    /// <inheritdoc />
    public IEnumerable<string> EnumerateDirectories(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateDirectories(directory).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <inheritdoc />
    public string ReadAllText(string path) => File.ReadAllText(path, Encoding.UTF8);

    /// <inheritdoc />
    public void WriteAllTextAtomic(string path, string content)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);
        var tmp = full + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.Write(content);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        File.Move(tmp, full, overwrite: true);
    }

    /// <inheritdoc />
    public void MoveFile(string source, string destination, bool overwrite) => File.Move(source, destination, overwrite);

    /// <inheritdoc />
    public void DeleteFile(string path) => File.Delete(path);

    /// <inheritdoc />
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    /// <inheritdoc />
    public string? ResolveFinalPath(string path)
    {
        using var handle = NativeMethods.CreateFileW(path, 0, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero,
            FileMode.Open, NativeMethods.FileFlagBackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return null;
        }

        var buffer = new char[1024];
        var len = NativeMethods.GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        if (len == 0 || len >= buffer.Length)
        {
            return null;
        }

        var result = new string(buffer, 0, (int)len);
        if (result.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
        {
            return @"\\" + result[8..];
        }

        return result.StartsWith(@"\\?\", StringComparison.Ordinal) ? result[4..] : result;
    }

    /// <inheritdoc />
    public bool IsFileLocked(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <inheritdoc />
    public DriveSpace? GetDriveSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            var drive = new DriveInfo(root);
            return drive.IsReady ? new DriveSpace(drive.TotalSize, drive.AvailableFreeSpace) : null;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public string? GetFileCompany(string path)
    {
        try
        {
            return File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).CompanyName : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public string? GetSignerSubject(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var cert = X509Certificate.CreateFromSignedFile(path);
            return cert.Subject;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void EnsureAdminOnlyDirectory(string path)
    {
        var dir = Directory.CreateDirectory(path);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        dir.SetAccessControl(security);
    }

    private static FileEntry ToEntry(FileInfo f) =>
        new(f.FullName, f.Length, f.LastWriteTimeUtc, f.Attributes.HasFlag(FileAttributes.ReparsePoint));

    private static class NativeMethods
    {
        public const uint FileFlagBackupSemantics = 0x02000000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, FileShare shareMode,
            IntPtr securityAttributes, FileMode creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, [Out] char[] filePath, uint filePathLength, uint flags);
    }
}
