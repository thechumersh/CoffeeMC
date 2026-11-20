using System.Runtime.InteropServices;

namespace CoffeeMC.App.Services
{
    public static class SystemInfoService
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        // Возвращает объём физической ОЗУ компьютера в мегабайтах, 0 — если определить не удалось
        public static long GetTotalPhysicalMemoryMb()
        {
            try
            {
                var status = new MEMORYSTATUSEX();
                status.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));

                if (GlobalMemoryStatusEx(ref status))
                {
                    return (long)(status.ullTotalPhys / (1024 * 1024));
                }
            }
            catch
            {
                // Если по какой-то причине системный вызов недоступен — просто не показываем это значение
            }

            return 0;
        }
    }
}