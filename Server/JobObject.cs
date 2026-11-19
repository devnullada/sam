using System.Runtime.InteropServices;

namespace ServiceManager.Server;

/// <summary>
/// Puts the current process in a Job Object with KILL_ON_JOB_CLOSE. Every process
/// started afterwards (grandchildren included) inherits the job, so when the server
/// dies by any route — crash, Kill, logoff — the kernel tears the whole tree down.
/// </summary>
static class JobObject
{
    // Never closed: the kernel closes it when this process exits, which is the point.
    // Closing it early (or wrapping it in a SafeHandle that gets finalized) kills every service.
    static IntPtr _job;

    public static void AttachSelf()
    {
        if (!OperatingSystem.IsWindows() || _job != IntPtr.Zero) return;

        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) { Warn("CreateJobObject"); return; }

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)size))
            { Warn("SetInformationJobObject"); CloseHandle(job); return; }
        }
        finally { Marshal.FreeHGlobal(ptr); }

        if (!AssignProcessToJobObject(job, GetCurrentProcess()))
        { Warn("AssignProcessToJobObject"); CloseHandle(job); return; }

        _job = job;
    }

    static void Warn(string api) =>
        Console.Error.WriteLine($"Job object setup failed at {api} (error {Marshal.GetLastWin32Error()}); services may outlive the server.");

    const int JobObjectExtendedLimitInformation = 9;
    const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetInformationJobObject(IntPtr hJob, int infoClass, IntPtr lpInfo, uint cbInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);
}
