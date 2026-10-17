using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace FFOne.Editor.Integration
{
    // A Windows job owns cargo and its server child. Its handle stays in the Editor process
    // across managed domain reloads and the OS closes it if the Editor exits or crashes.
    public static class DevelopmentProcess
    {
        public static long Start(string executable, string[] arguments, string directory, string logPath, out int processId)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException("The local RustyFusion launcher currently supports Windows.");
            var job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) throw new Win32Exception();
            IntPtr output = IntPtr.Zero, input = IntPtr.Zero;
            var process = new ProcessInformation();
            try
            {
                var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE
                if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>())) throw new Win32Exception();
                var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Inherit = true };
                output = CreateFile(logPath, 0x40000000, 7, ref security, 2, 0x80, IntPtr.Zero);
                input = CreateFile("NUL", 0x80000000, 3, ref security, 3, 0x80, IntPtr.Zero);
                if (output == new IntPtr(-1) || input == new IntPtr(-1)) throw new Win32Exception();
                var startup = new StartupInfo {
                    Size = Marshal.SizeOf<StartupInfo>(), Flags = 0x101, ShowWindow = 0,
                    StandardInput = input, StandardOutput = output, StandardError = output
                };
                var command = new StringBuilder(Quote(executable) + " " + string.Join(" ", arguments.Select(Quote)));
                // Start suspended so no child can escape the job; a hidden private console
                // allows a later Ctrl+C without signalling an unrelated terminal or server.
                if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, true, 0x14,
                    IntPtr.Zero, directory, ref startup, out process)) throw new Win32Exception();
                if (!AssignProcessToJobObject(job, process.Process)) throw new Win32Exception();
                if (ResumeThread(process.Thread) == uint.MaxValue) throw new Win32Exception();
                processId = process.Id;
                return job.ToInt64();
            }
            catch
            {
                if (process.Process != IntPtr.Zero) TerminateProcess(process.Process, 1);
                CloseHandle(job);
                throw;
            }
            finally
            {
                if (output != IntPtr.Zero && output != new IntPtr(-1)) CloseHandle(output);
                if (input != IntPtr.Zero && input != new IntPtr(-1)) CloseHandle(input);
                if (process.Thread != IntPtr.Zero) CloseHandle(process.Thread);
                if (process.Process != IntPtr.Zero) CloseHandle(process.Process);
            }
        }

        public static bool IsRunning(long job)
        {
            if (job == 0) return false;
            return QueryInformationJobObject(new IntPtr(job), 1, out var accounting,
                (uint)Marshal.SizeOf<Accounting>(), IntPtr.Zero) && accounting.ActiveProcesses > 0;
        }

        public static void Close(long job)
        {
            if (job != 0) CloseHandle(new IntPtr(job));
        }

        public static void Interrupt(long job, int processId, string helperPath)
        {
            using var owner = Process.GetProcessById(processId);
            if (!IsProcessInJob(owner.Handle, new IntPtr(job), out bool owned) || !owned) return;
            long started = owner.StartTime.ToUniversalTime().Ticks;
            // This helper attaches only to the private console created above. Sending Ctrl+C
            // lets RustyFusion save connected players and flush logs before it exits.
            File.WriteAllText(ContentPaths.FilePath(helperPath), @"param([int]$TargetProcess, [long]$Started)
$ErrorActionPreference = 'Stop'
try {
    $ownedProcess = [Diagnostics.Process]::GetProcessById($TargetProcess)
    $ownedHandle = $ownedProcess.Handle
    if ($ownedProcess.StartTime.ToUniversalTime().Ticks -ne $Started) { exit }
} catch { exit }
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FFOneConsoleSignal {
    [DllImport(""kernel32.dll"", SetLastError=true)] public static extern bool AttachConsole(uint id);
    [DllImport(""kernel32.dll"")] public static extern bool FreeConsole();
    [DllImport(""kernel32.dll"")] public static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
    [DllImport(""kernel32.dll"")] public static extern bool GenerateConsoleCtrlEvent(uint signal, uint group);
}
'@
[FFOneConsoleSignal]::FreeConsole() | Out-Null
if ([FFOneConsoleSignal]::AttachConsole($TargetProcess)) {
    [FFOneConsoleSignal]::SetConsoleCtrlHandler([IntPtr]::Zero, $true) | Out-Null
    [FFOneConsoleSignal]::GenerateConsoleCtrlEvent(0, 0) | Out-Null
    Start-Sleep -Milliseconds 250
    [FFOneConsoleSignal]::FreeConsole() | Out-Null
}
");
            var info = new ProcessStartInfo {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Quote(helperPath) + " " + processId + " " + started,
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(info)?.Dispose();
        }

        // CommandLineToArgvW / CRT escaping, including embedded quotes and trailing slashes.
        public static string Quote(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') result.Append('\\', slashes * 2 + 1);
                else result.Append('\\', slashes);
                result.Append(c); slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }

        [StructLayout(LayoutKind.Sequential)] struct SecurityAttributes { public int Length; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool Inherit; }
        [StructLayout(LayoutKind.Sequential)] struct ProcessInformation { public IntPtr Process, Thread; public int Id, ThreadId; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct StartupInfo
        {
            public int Size; public string Reserved, Desktop, Title;
            public int X, Y, XSize, YSize, XChars, YChars, FillAttribute, Flags;
            public short ShowWindow, ReservedSize; public IntPtr ReservedBytes, StandardInput, StandardOutput, StandardError;
        }
        [StructLayout(LayoutKind.Sequential)] struct BasicLimits
        {
            public long ProcessUserTime, JobUserTime; public uint Flags;
            public UIntPtr MinimumWorkingSet, MaximumWorkingSet; public uint ActiveProcessLimit;
            public UIntPtr Affinity; public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits
        {
            public BasicLimits Basic; public IoCounters Io;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemory, PeakJobMemory;
        }
        [StructLayout(LayoutKind.Sequential)] struct Accounting
        {
            public long TotalUserTime, TotalKernelTime, PeriodUserTime, PeriodKernelTime;
            public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int type, ref ExtendedLimits information, uint length);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool QueryInformationJobObject(IntPtr job, int type, out Accounting information, uint length, IntPtr returned);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateFile(string path, uint access, uint share, ref SecurityAttributes security, uint disposition, uint attributes, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation process);
        [DllImport("kernel32.dll", SetLastError = true)] static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateProcess(IntPtr process, uint code);
    }
}
