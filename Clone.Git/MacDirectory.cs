using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Clone.Git;

[SupportedOSPlatform("macos")]
internal sealed partial class MacDirectory : IDisposable
{
    private const int DirectoryFlags = 0x100000 | 0x100 | 0x1000000;
    private readonly SafeFileHandle _handle;
    private int Descriptor => _handle.DangerousGetHandle().ToInt32();

    private MacDirectory(int descriptor)
    {
        if (descriptor < 0) throw Failure("Open directory");
        _handle = new SafeFileHandle(descriptor, true);
    }

    public string Path
    {
        get
        {
            var bytes = new byte[1024];
            if (GetPath(Descriptor, 50, bytes) < 0) throw Failure("Resolve directory");
            return Encoding.UTF8.GetString(bytes, 0, Array.IndexOf(bytes, (byte)0));
        }
    }

    public static MacDirectory OpenRoot(string path, bool create)
    {
        if (!System.IO.Path.IsPathFullyQualified(path) || path == "/") throw new IOException("Choose an absolute project directory other than the filesystem root.");
        var current = new MacDirectory(Open("/", DirectoryFlags, 0));
        try
        {
            foreach (var segment in System.IO.Path.GetFullPath(path).Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                var next = current.Child(segment, create);
                current.Dispose();
                current = next;
                current.CheckOwnership(allowSticky: true, requireUser: false);
            }
            current.CheckOwnership(allowSticky: false, requireUser: true);
            return current;
        }
        catch { current.Dispose(); throw; }
    }

    public MacDirectory Child(string name, bool create = false)
    {
        CheckName(name);
        if (create && Mkdir(Descriptor, name, 0x1c0) < 0 && Marshal.GetLastPInvokeError() != 17) throw Failure("Create directory");
        return new MacDirectory(OpenAt(Descriptor, name, DirectoryFlags, 0));
    }

    public MacDirectory CreateExclusive(string name)
    {
        CheckName(name);
        if (Mkdir(Descriptor, name, 0x1c0) < 0) throw Failure("Create staging directory");
        return Child(name);
    }

    public void CheckOwnership(bool allowSticky = false, bool requireUser = true)
    {
        if (ReadStat(Descriptor, out var stat) < 0) throw Failure("Inspect directory");
        var uid = GetUser();
        if ((requireUser && stat.User != uid) || (!requireUser && stat.User != 0 && stat.User != uid)) throw new IOException("Directories must be owned by the current user or a trusted system owner.");
        if ((stat.Mode & 0x12) != 0 && !(allowSticky && (stat.Mode & 0x200) != 0 && stat.User == 0))
            throw new IOException("Shared writable directories are not supported. Use a private project/configuration root.");
    }

    public bool Contains(string name)
    {
        CheckName(name);
        // O_SYMLINK inspects the entry itself, including dangling links.
        var fd = OpenAt(Descriptor, name, 0x200000 | 0x1000000 | 0x4, 0);
        if (fd >= 0) { using var handle = new SafeFileHandle(fd, true); return true; }
        if (Marshal.GetLastPInvokeError() == 2) return false;
        throw Failure("Inspect destination");
    }

    public void Publish(string name, MacDirectory destination, string destinationName)
    {
        CheckName(name);
        CheckName(destinationName);
        if (RenameExclusive(Descriptor, name, destination.Descriptor, destinationName, 4) < 0)
            throw new IOException("The destination changed or already exists; no existing clone was replaced.");
    }

    public string? ReadText(string name)
    {
        CheckName(name);
        var fd = OpenAt(Descriptor, name, 0x100 | 0x1000000 | 0x4, 0);
        if (fd < 0)
        {
            if (Marshal.GetLastPInvokeError() == 2) return null;
            throw Failure("Read configuration");
        }
        using var handle = new SafeFileHandle(fd, true);
        if (ReadStat(fd, out var stat) < 0 || (stat.Mode & 0xf000) != 0x8000 || stat.User != GetUser() || (stat.Mode & 0x12) != 0 || stat.Size > 16384)
            throw new IOException("Configuration must be a small, user-owned regular file without shared write permissions.");
        using var stream = new FileStream(handle, FileAccess.Read);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024);
        var buffer = new char[16385];
        var length = reader.ReadBlock(buffer, 0, buffer.Length);
        if (length > 16384) throw new IOException("Configuration exceeds the size limit.");
        return new string(buffer, 0, length);
    }

    public void WriteText(string name, string content)
    {
        CheckName(name);
        CheckOwnership();
        var temporary = ".config-" + Guid.NewGuid().ToString("N");
        var fd = OpenAt(Descriptor, temporary, 0x1 | 0x200 | 0x800 | 0x100 | 0x1000000, 0x180);
        if (fd < 0) throw Failure("Create configuration");
        try
        {
            using (var handle = new SafeFileHandle(fd, true))
            using (var stream = new FileStream(handle, FileAccess.Write))
            {
                stream.Write(Encoding.UTF8.GetBytes(content));
                stream.Flush(true);
            }
            if (Rename(Descriptor, temporary, Descriptor, name) < 0) throw Failure("Save configuration");
        }
        finally { Unlink(Descriptor, temporary, 0); }
    }

    public void RemoveOwnedTree(string name)
    {
        CheckName(name);
        using var child = Child(name);
        child.CheckOwnership();
        child.Empty();
        if (Unlink(Descriptor, name, 0x80) < 0) throw Failure("Remove staging directory");
    }

    private void Empty()
    {
        var fd = Duplicate(Descriptor);
        if (fd < 0) throw Failure("Read staging directory");
        var listing = OpenListing(fd);
        if (listing == 0) { using var handle = new SafeFileHandle(fd, true); throw Failure("Read staging directory"); }
        try
        {
            while (true)
            {
                Marshal.SetLastPInvokeError(0);
                var entry = ReadEntry(listing);
                if (entry == 0)
                {
                    if (Marshal.GetLastPInvokeError() != 0) throw Failure("Enumerate staging directory");
                    break;
                }
                var name = Marshal.PtrToStringUTF8(entry)!;
                if (name is "." or "..") continue;
                var childFd = OpenAt(Descriptor, name, DirectoryFlags, 0);
                if (childFd >= 0)
                {
                    using var child = new MacDirectory(childFd);
                    child.CheckOwnership();
                    child.Empty();
                    if (Unlink(Descriptor, name, 0x80) < 0) throw Failure("Clean staging subdirectory");
                }
                else
                {
                    // Unlink relative to the anchored parent; never follow repository symlinks.
                    if (Unlink(Descriptor, name, 0) < 0) throw Failure("Clean staging entry");
                }
            }
        }
        finally { CloseListing(listing); }
    }

    private static void CheckName(string name)
    {
        if (name is "" or "." or ".." || name.Contains('/') || name.Contains('\0')) throw new ArgumentException("Invalid directory entry.");
    }
    private static IOException Failure(string operation) => new($"{operation} failed (OS error {Marshal.GetLastPInvokeError()}). Check ownership, permissions, and symlinks.");
    public void Dispose() => _handle.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct Stat
    {
        public uint Mode;
        public uint User;
        public long Size;
    }
    [LibraryImport("clone_native", EntryPoint = "clone_open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string path, int flags, int mode);
    [LibraryImport("clone_native", EntryPoint = "clone_openat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenAt(int fd, string path, int flags, int mode);
    [LibraryImport("libSystem.B.dylib", EntryPoint = "mkdirat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Mkdir(int fd, string path, int mode);
    [LibraryImport("libSystem.B.dylib", EntryPoint = "unlinkat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Unlink(int fd, string path, int flags);
    [LibraryImport("libSystem.B.dylib", EntryPoint = "renameatx_np", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int RenameExclusive(int source, string name, int destination, string target, uint flags);
    [LibraryImport("libSystem.B.dylib", EntryPoint = "renameat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Rename(int source, string name, int destination, string target);
    [LibraryImport("clone_native", EntryPoint = "clone_getpath", SetLastError = true)]
    private static partial int GetPath(int fd, int command, [Out] byte[] path);
    [LibraryImport("clone_native", EntryPoint = "clone_stat", SetLastError = true)]
    private static partial int ReadStat(int fd, out Stat stat);
    [LibraryImport("libSystem.B.dylib", EntryPoint = "geteuid")]
    internal static partial uint GetUser();
    [LibraryImport("libSystem.B.dylib", EntryPoint = "dup", SetLastError = true)]
    private static partial int Duplicate(int fd);
    [LibraryImport("clone_native", EntryPoint = "clone_opendir", SetLastError = true)]
    private static partial nint OpenListing(int fd);
    [LibraryImport("clone_native", EntryPoint = "clone_readdir", SetLastError = true)]
    private static partial nint ReadEntry(nint listing);
    [LibraryImport("libSystem.B.dylib", EntryPoint = "closedir", SetLastError = true)]
    private static partial int CloseListing(nint listing);
}
