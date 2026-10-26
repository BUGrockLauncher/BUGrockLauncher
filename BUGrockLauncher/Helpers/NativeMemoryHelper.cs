using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BUGrockLauncher.Helpers
{
    public static class NativeMemoryHelper
    {
        [DllImport("psapi.dll")]
        private static extern int EmptyWorkingSet(IntPtr hwProc);

        public static void TrimWorkingSet()
        {
            try
            {
                using var currentProcess = Process.GetCurrentProcess();
                EmptyWorkingSet(currentProcess.Handle);
            }
            catch
            {
            }
        }
    }
}
