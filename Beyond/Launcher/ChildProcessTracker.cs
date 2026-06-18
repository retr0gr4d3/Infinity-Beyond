using System;
using System.Runtime.InteropServices;

namespace Launcher
{
    // Ties spawned child processes (the embedded game) to the launcher's
    // lifetime via a Windows Job Object configured with KILL_ON_JOB_CLOSE.
    //
    // The job handle is held for the launcher's whole lifetime and never
    // closed explicitly. When the launcher process exits — normally, or via a
    // crash, or being killed — the OS closes all its handles, the job handle
    // among them, and the job then terminates every process assigned to it.
    // This guarantees the game can never outlive the launcher and orphan the
    // :28900 server socket.
    //
    // Windows 8+ allows a process to belong to multiple (nested) jobs, so this
    // works even though Unity may place itself in its own job.
    internal static class ChildProcessTracker
    {
        private static readonly IntPtr s_jobHandle;
        private static readonly bool s_ready;

        static ChildProcessTracker()
        {
            try
            {
                // Anonymous job (no name) so nothing else can open it and keep
                // it alive past our process.
                s_jobHandle = CreateJobObject(IntPtr.Zero, null);
                if (s_jobHandle == IntPtr.Zero) return;

                var extended = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                {
                    BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                    {
                        LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                    }
                };

                int length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
                IntPtr ptr = Marshal.AllocHGlobal(length);
                try
                {
                    Marshal.StructureToPtr(extended, ptr, false);
                    s_ready = SetInformationJobObject(
                        s_jobHandle,
                        JobObjectInfoType.ExtendedLimitInformation,
                        ptr,
                        (uint)length);
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
            catch
            {
                s_ready = false;
            }
        }

        // Assign a started process to the job. Call right after Process.Start so
        // the game's own child processes inherit job membership too.
        public static bool AddProcess(IntPtr processHandle)
        {
            if (!s_ready || s_jobHandle == IntPtr.Zero || processHandle == IntPtr.Zero) return false;
            return AssignProcessToJobObject(s_jobHandle, processHandle);
        }

        private enum JobObjectInfoType
        {
            ExtendedLimitInformation = 9
        }

        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
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
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);
    }
}
