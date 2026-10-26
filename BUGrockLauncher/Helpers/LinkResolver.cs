using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BUGrockLauncher
{
    public static class LinkResolver
    {
        private const int FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private const int FILE_SHARE_ALL = 0x07;
        private const int OPEN_EXISTING = 3;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string path,
            int desiredAccess,
            int shareMode,
            IntPtr securityAttributes,
            int creationDisposition,
            int flags,
            IntPtr templateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetFinalPathNameByHandle(
            SafeFileHandle handle,
            StringBuilder buffer,
            int bufferSize,
            int flags);

        public static string Resolve(string directory)
        {
            using SafeFileHandle handle = CreateFile(
                directory,
                0,
                FILE_SHARE_ALL,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                throw new IOException("Could not open directory");
            }

            var buffer = new StringBuilder(1024);
            int result = GetFinalPathNameByHandle(handle, buffer, buffer.Capacity, 0);

            if (result >= buffer.Capacity)
            {
                buffer.EnsureCapacity(result + 1);
                result = GetFinalPathNameByHandle(handle, buffer, buffer.Capacity, 0);
            }

            if (result <= 0)
            {
                throw new IOException("Could not resolve final path");
            }

            string raw = buffer.ToString();

            return raw.StartsWith(@"\\?\") ? raw.Substring(4) : raw;
        }
    }
}
