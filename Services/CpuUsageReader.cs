using System.Runtime.InteropServices;

namespace GasparSystemHealth.Services;

public sealed class CpuUsageReader
{
    private FileTime _idle;
    private FileTime _kernel;
    private FileTime _user;
    private bool _hasBaseline;

    public double ReadUsagePercent()
    {
        if (!GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user))
        {
            return 0;
        }

        if (!_hasBaseline)
        {
            _idle = idle;
            _kernel = kernel;
            _user = user;
            _hasBaseline = true;
            return 0;
        }

        ulong idleDiff = idle.ToUInt64() - _idle.ToUInt64();
        ulong kernelDiff = kernel.ToUInt64() - _kernel.ToUInt64();
        ulong userDiff = user.ToUInt64() - _user.ToUInt64();

        _idle = idle;
        _kernel = kernel;
        _user = user;

        ulong total = kernelDiff + userDiff;
        if (total == 0)
        {
            return 0;
        }

        double busy = total - idleDiff;
        return Math.Clamp(Math.Round((busy / total) * 100d, 1), 0, 100);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct FileTime
    {
        private readonly uint _low;
        private readonly uint _high;

        public ulong ToUInt64() => ((ulong)_high << 32) | _low;
    }
}
