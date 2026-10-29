using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace FluentSblock.Services;

/// <summary>Restarts the app with a different integrity level (Admin ↔ User).</summary>
public static class ElevationService
{
    private static string ExePath =>
        Environment.ProcessPath
        ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? "";

    private static string PreservedArgs()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToList();
        args.RemoveAll(a => a.Equals("--user", StringComparison.OrdinalIgnoreCase));
        return string.Join(" ", args.Select(Quote));
    }

    private static string Quote(string a) =>
        a.Contains(' ') || a.Contains('"') ? "\"" + a.Replace("\"", "\"\"") + "\"" : a;

    /// <summary>Relaunches elevated via UAC. Returns false when the user cancels.</summary>
    public static bool RestartAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ExePath, PreservedArgs())
            {
                UseShellExecute = true,
                Verb = "runas"
            });
            Application.Current.Shutdown();
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Relaunches without elevation. First tries the Explorer shell token
    /// (medium integrity, silent, exact args); falls back to runas with the
    /// basic-user trust level (no UAC prompt, exact args).
    /// </summary>
    public static bool RestartAsUser()
    {
        string args = PreservedArgs();
        var before = new HashSet<int>();
        try
        {
            foreach (var p in Process.GetProcessesByName("FluentSblock"))
            {
                using (p) before.Add(p.Id);
            }
        }
        catch { }
        if (TryRestartAsUserViaToken(args)
            || TryRestartAsUserViaRunas(args, before))
        {
            Application.Current.Shutdown();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Creates the child directly with a duplicated Explorer token.
    /// Needs SeImpersonate/SeIncreaseQuota: present when the caller is
    /// elevated (the normal case here), absent for standard users.
    /// </summary>
    private static bool TryRestartAsUserViaToken(string args)
    {
        IntPtr hProc = IntPtr.Zero, hTok = IntPtr.Zero,
               hPrimary = IntPtr.Zero, env = IntPtr.Zero,
               hChildProc = IntPtr.Zero, hChildThread = IntPtr.Zero;
        try
        {
            uint pid = ShellProcessId();
            if (pid == 0) return false;
            hProc = OpenProcess(PROCESS_QUERY_INFORMATION, false, pid);
            if (hProc == IntPtr.Zero) return false;
            if (!OpenProcessToken(hProc, TOKEN_DUPLICATE | TOKEN_QUERY, out hTok)
                || hTok == IntPtr.Zero) return false;
            if (!DuplicateTokenEx(hTok, MAXIMUM_ALLOWED, IntPtr.Zero,
                    SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                    TOKEN_TYPE.TokenPrimary, out hPrimary)
                || hPrimary == IntPtr.Zero) return false;
            CreateEnvironmentBlock(out env, hPrimary, false); // best effort
            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf<STARTUPINFO>();
            si.lpDesktop = "winsta0\\default";
            string cmd = "\"" + ExePath + "\"" + (args.Length > 0 ? " " + args : "");
            if (!CreateProcessWithTokenW(hPrimary, 0, null, cmd,
                    NORMAL_PRIORITY_CLASS | CREATE_UNICODE_ENVIRONMENT,
                    env, Path.GetDirectoryName(ExePath),
                    ref si, out PROCESS_INFORMATION pi)) return false;
            hChildProc = pi.hProcess;
            hChildThread = pi.hThread;
            return true;
        }
        catch { return false; }
        finally
        {
            foreach (var h in new[] { hChildThread, hChildProc, hPrimary, hTok, hProc })
                if (h != IntPtr.Zero) { try { CloseHandle(h); } catch { } }
            if (env != IntPtr.Zero) { try { DestroyEnvironmentBlock(env); } catch { } }
        }
    }

    /// <summary>
    /// Fallback: runas with the basic-user trust level. Needs no caller
    /// privileges and never prompts; waits for the child to appear so a
    /// quoting failure never kills the running instance for nothing.
    /// </summary>
    private static bool TryRestartAsUserViaRunas(string args, HashSet<int> before)
    {
        try
        {
            // Program operand is one quoted unit: "\"exe\" args".
            string program = ("\\\"" + ExePath + "\\\" " + args).TrimEnd();
            var psi = new ProcessStartInfo("runas.exe", "/trustlevel:0x20000 \"" + program + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var runas = Process.Start(psi);
            if (runas == null) return false;
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(10))
            {
                try
                {
                    foreach (var c in Process.GetProcessesByName("FluentSblock"))
                    {
                        using (c)
                        {
                            if (!before.Contains(c.Id)) return true;
                        }
                    }
                }
                catch { }
                Thread.Sleep(300);
            }
            return false;
        }
        catch { return false; }
    }

    /// <summary>PID of the medium-integrity Explorer shell in this session.</summary>
    private static uint ShellProcessId()
    {
        try
        {
            IntPtr shell = GetShellWindow();
            if (shell != IntPtr.Zero)
            {
                GetWindowThreadProcessId(shell, out uint pid);
                if (pid != 0) return pid;
            }
        }
        catch { }
        try
        {
            int session = Process.GetCurrentProcess().SessionId;
            var explorer = Process.GetProcessesByName("explorer")
                .FirstOrDefault(p => p.SessionId == session);
            if (explorer != null) return (uint)explorer.Id;
        }
        catch { }
        return 0;
    }

    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint MAXIMUM_ALLOWED = 0x02000000;
    private const uint NORMAL_PRIORITY_CLASS = 0x00000020;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    private enum SECURITY_IMPERSONATION_LEVEL
    {
        SecurityAnonymous = 0,
        SecurityIdentification = 1,
        SecurityImpersonation = 2,
        SecurityDelegation = 3
    }

    private enum TOKEN_TYPE
    {
        TokenPrimary = 1,
        TokenImpersonation = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput, hStdOutput, hStdError;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("advapi32.dll")]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll")]
    private static extern bool DuplicateTokenEx(IntPtr hExistingToken, uint dwDesiredAccess,
        IntPtr lpTokenAttributes, SECURITY_IMPERSONATION_LEVEL ImpersonationLevel,
        TOKEN_TYPE TokenType, out IntPtr phNewToken);

    [DllImport("userenv.dll")]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll")]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessWithTokenW(IntPtr hToken, uint dwLogonFlags,
        string? lpApplicationName, string lpCommandLine, uint dwCreationFlags,
        IntPtr lpEnvironment, string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);
}
