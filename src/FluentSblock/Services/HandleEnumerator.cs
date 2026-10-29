using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using FluentSblock.Models;

namespace FluentSblock.Services;

/// <summary>
/// Enumerates open File handles and filters by volume.
/// Technique follows handle64-spec.md: NtQuerySystemInformation(SystemExtendedHandleInformation)
/// + DuplicateHandle + NtQueryObject. Read-only, needs SeDebugPrivilege for other users' processes.
/// </summary>
public static class HandleEnumerator
{
    private const int SystemExtendedHandleInformation = 64;
    private const int ObjectNameInformation = 1;
    private const int ObjectTypeInformation = 2;
    private const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;
    private const uint PROCESS_DUP_HANDLE = 0x0040;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint DUPLICATE_SAME_ACCESS = 0x2;
    private const uint SEM_FAILCRITICALERRORS = 0x0001;
    private const uint STATUS_BUFFER_OVERFLOW = 0xC0000023;

    /// <summary>Per-name-query timeout in ms (NtQueryObject can block on pipes). 20..5000.</summary>
    public static int QueryNameTimeoutMs
    {
        get => _queryNameTimeoutMs;
        set => _queryNameTimeoutMs = Math.Clamp(value, 20, 5000);
    }
    private static int _queryNameTimeoutMs = 100;

    [DllImport("ntdll.dll")]
    private static extern uint NtQuerySystemInformation(int SystemInformationClass,
        IntPtr SystemInformation, uint SystemInformationLength, out uint ReturnLength);

    [DllImport("ntdll.dll")]
    private static extern uint NtQueryObject(IntPtr Handle, int ObjectInformationClass,
        IntPtr ObjectInformation, uint ObjectInformationLength, out uint ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(IntPtr hSourceProcessHandle, IntPtr hSourceHandle,
        IntPtr hTargetProcessHandle, out IntPtr lpTargetHandle,
        uint dwDesiredAccess, bool bInheritHandle, uint dwOptions);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetThreadErrorMode(uint dwNewMode, out uint lpOldMode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDeviceW(string? lpDevice, System.Text.StringBuilder lpTargetPath, uint ucchMax);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetGetUniversalNameW(string lpLocalPath, int dwInfoLevel,
        IntPtr lpBuffer, ref uint lpBufferSize);

    private const int UNIVERSAL_NAME_INFO_LEVEL = 1;
    private const int ERROR_MORE_DATA = 234;
    private const int NO_ERROR = 0;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint dwFlags,
        System.Text.StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LookupPrivilegeValueW(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass,
        IntPtr TokenInformation, uint TokenInformationLength, out uint ReturnLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LookupAccountSidW(string? lpSystemName, byte[] Sid,
        System.Text.StringBuilder lpName, ref uint cchName,
        System.Text.StringBuilder lpReferencedDomainName, ref uint cchReferencedDomainName,
        out int peUse);

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SID_AND_ATTRIBUTES { public IntPtr Sid; public uint Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_USER { public SID_AND_ATTRIBUTES User; }

    private const int TokenUserClass = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX
    {
        public IntPtr Object;
        public UIntPtr UniqueProcessId;
        public UIntPtr HandleValue;
        public uint GrantedAccess;
        public ushort CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public uint HandleAttributes;
        public uint Reserved;
    }

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    public static void EnableDebugPrivilege()
    {
        try
        {
            if (!LookupPrivilegeValueW(null, "SeDebugPrivilege", out var luid)) return;
            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Privileges = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED }
            };
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token)) return;
            try { AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero); }
            finally { CloseHandle(token); }
        }
        catch { /* best effort */ }
    }

    public static Task<List<LockHolder>> GetHoldersForDriveAsync(
        string driveLetter, IProgress<string>? progress, CancellationToken ct)
    {
        // driveLetter: "E:" — matches "E:\" prefix after NT->DOS conversion.
        // Mapped network drives never resolve to "Z:\...": handles appear as
        // \Device\Mup\... or UNC, so match those forms too.
        string drive = driveLetter.TrimEnd('\\');
        string prefix = drive + "\\";
        string? unc = TryGetUniversalName(drive + "\\");
        string? uncPrefix = unc == null ? null : unc + "\\";
        string? mupPrefix = unc == null ? null : @"\Device\Mup\" + unc.TrimStart('\\') + "\\";
        string? devPrefix = TryGetDevicePrefix(drive);
        return ScanAsync(
            dos => dos.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                   dos.Equals(drive, StringComparison.OrdinalIgnoreCase) ||
                   (uncPrefix != null && (dos.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase) ||
                                          dos.Equals(unc, StringComparison.OrdinalIgnoreCase))) ||
                   (mupPrefix != null && dos.StartsWith(mupPrefix, StringComparison.OrdinalIgnoreCase)) ||
                   (devPrefix != null && dos.StartsWith(devPrefix, StringComparison.OrdinalIgnoreCase)),
            progress, ct);
    }

    public static Task<List<LockHolder>> GetHoldersForPathAsync(
        string path, IProgress<string>? progress, CancellationToken ct)
    {
        // Exact file or its parent dir prefix. Falls back to handle scan
        // (Restart Manager is tried first by FileLockChecker).
        string full;
        try { full = Path.GetFullPath(path).TrimEnd('\\'); }
        catch { full = path; }
        // Handles are NT/UNC form, never "Z:\...": match every equivalent form.
        var candidates = new List<string> { full };
        if (full.Length >= 2 && full[1] == ':')
        {
            string? dev = TryGetDevicePrefix(full[..2]);
            if (dev != null) candidates.Add(dev + full[2..].TrimStart('\\'));
        }
        string? uncName = full.StartsWith(@"\\", StringComparison.Ordinal)
            ? full
            : TryGetUniversalName(full);
        if (uncName != null)
        {
            candidates.Add(uncName);
            candidates.Add(@"\Device\Mup\" + uncName.TrimStart('\\'));
        }
        return ScanAsync(
            dos => candidates.Any(c => dos.Equals(c, StringComparison.OrdinalIgnoreCase) ||
                                       dos.StartsWith(c + "\\", StringComparison.OrdinalIgnoreCase)),
            progress, ct);
    }

    private static Task<List<LockHolder>> ScanAsync(
        Func<string, bool> match, IProgress<string>? progress, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            EnableDebugPrivilege();
            // Like handle64 main: no error dialogs while touching every process.
            SetThreadErrorMode(SEM_FAILCRITICALERRORS, out _);
            var dosMap = BuildDosDeviceMap(); // longest prefix first
            var raw = QueryAllHandles(); // throws with resx message when the snapshot fails
            var progressPattern = L10n.Get("ScanProgress");
            var result = new List<LockHolder>();
            var userCache = new Dictionary<uint, string>();
            var nameCache = new Dictionary<uint, string>();
            int n = 0;
            // One OpenProcess per PID (handle64 re-opens only on PID change):
            // ~200 opens instead of one per handle (~120k).
            foreach (var group in raw.GroupBy(h => h.Pid))
            {
                ct.ThrowIfCancellationRequested();
                IntPtr src = OpenProcess(PROCESS_DUP_HANDLE | PROCESS_QUERY_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION,
                    false, group.Key);
                if (src == IntPtr.Zero)
                    src = OpenProcess(PROCESS_DUP_HANDLE, false, group.Key);
                if (src == IntPtr.Zero) { n += group.Count(); continue; }
                try
                {
                    if (!userCache.TryGetValue(group.Key, out var user))
                    {
                        user = TryGetProcessUser(src);
                        userCache[group.Key] = user;
                    }
                    if (!nameCache.TryGetValue(group.Key, out var pname))
                    {
                        // Name from the already-open handle: works for protected
                        // processes where a fresh full-access open would fail.
                        pname = TryGetProcessNameFromHandle(src) ?? TryGetProcessName(group.Key);
                        nameCache[group.Key] = pname;
                    }
                    foreach (var h in group)
                    {
                        ct.ThrowIfCancellationRequested();
                        if ((++n & 0xFFF) == 0)
                            progress?.Report(string.Format(progressPattern, n, raw.Count));
                        string? dos = ResolveHandleDosPath(src, h, dosMap);
                        if (dos == null || !match(dos)) continue;
                        result.Add(new LockHolder(pname, (int)h.Pid, user,
                            $"0x{h.HandleValue.ToUInt64():X08}", dos, $"0x{h.GrantedAccess:X08}"));
                    }
                }
                finally { CloseHandle(src); }
            }
            // Same file open several times: show only the first handle.
            result = result.DistinctBy(h => h.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
            result.Sort((a, b) => string.Compare(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase));
            return result;
        }, ct);
    }

    // ---- internals ----

    private sealed record RawHandle(uint Pid, UIntPtr HandleValue, uint GrantedAccess);

    private static List<RawHandle> QueryAllHandles()
    {
        // Probe first for the exact size (handle64 FUN_140007a40 calls class 64
        // with a NULL buffer), then realloc-loop in case the table grew.
        uint len = 1 << 20; // 1MB fallback
        uint probe = NtQuerySystemInformation(SystemExtendedHandleInformation, IntPtr.Zero, 0, out uint need);
        if (probe == STATUS_INFO_LENGTH_MISMATCH && need > 0)
            len = need + 65536;
        IntPtr buf = IntPtr.Zero;
        try
        {
            while (true)
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
                buf = Marshal.AllocHGlobal((int)Math.Min(len, 256 * 1024 * 1024));
                uint status = NtQuerySystemInformation(SystemExtendedHandleInformation, buf, len, out uint retLen);
                if (status == STATUS_INFO_LENGTH_MISMATCH)
                {
                    len = Math.Max(retLen + 65536, len * 2);
                    continue;
                }
                if (status != 0)
                    throw new InvalidOperationException(string.Format(L10n.Get("ScanFailedHandles"), status));
                break;
            }

            long numHandles = Marshal.ReadIntPtr(buf).ToInt64();
            IntPtr entryPtr = IntPtr.Add(buf, IntPtr.Size * 2); // skip NumberOfHandles + Reserved
            int entrySize = Marshal.SizeOf<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>();
            var list = new List<RawHandle>((int)Math.Min(numHandles, 200000));
            for (long i = 0; i < numHandles; i++)
            {
                var e = Marshal.PtrToStructure<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>(
                    IntPtr.Add(entryPtr, (int)(i * entrySize)));
                uint pid = e.UniqueProcessId.ToUInt32();
                // Type and name resolved lazily per PID group (only File matters).
                list.Add(new RawHandle(pid, e.HandleValue, e.GrantedAccess));
            }
            return list;
        }
        finally
        {
            if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
        }
    }

    private static string? ResolveHandleDosPath(IntPtr src, RawHandle h,
        List<KeyValuePair<string, string>> dosMap)
    {
        if (!DuplicateHandle(src, (IntPtr)h.HandleValue.ToUInt64(),
                GetCurrentProcess(), out IntPtr dup, 0, false, DUPLICATE_SAME_ACCESS))
            return null;
        string type = QueryObjectType(dup);
        if (!type.Equals("File", StringComparison.OrdinalIgnoreCase))
        {
            CloseHandle(dup);
            return null;
        }
        // Ownership of dup moves to the worker: it always closes dup, even on timeout.
        string? nt = QueryObjectNameTimeout(dup);
        if (string.IsNullOrEmpty(nt)) return null;
        if (nt.StartsWith(@"\Device\Mup\", StringComparison.OrdinalIgnoreCase))
            return @"\\" + nt[@"\Device\Mup\".Length..]; // UNC form: matches user input style
        foreach (var kv in dosMap)
        {
            if (nt.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                return kv.Value + nt[kv.Key.Length..];
        }
        return null; // other device (e.g. \Device\CNG), not a volume file
    }

    private static string QueryObjectType(IntPtr h)
    {
        IntPtr buf = Marshal.AllocHGlobal(4096);
        try
        {
            uint status = NtQueryObject(h, ObjectTypeInformation, buf, 4096, out _);
            if (status != 0) return "";
            var us = Marshal.PtrToStructure<UNICODE_STRING>(buf);
            if (us.Length == 0 || us.Buffer == IntPtr.Zero) return "";
            return Marshal.PtrToStringUni(us.Buffer, us.Length / 2) ?? "";
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static string? QueryObjectNameTimeout(IntPtr dup)
    {
        // NtQueryObject(ObjectNameInformation) can block indefinitely on pipes.
        // A pooled worker owns dup and always closes it; on timeout the worker is
        // abandoned and closes dup when the kernel call eventually returns.
        string? name = null;
        var task = Task.Run(() =>
        {
            try { name = QueryObjectNameGrowable(dup); }
            catch { /* treat as unresolvable */ }
            finally { CloseHandle(dup); }
        });
        if (!task.Wait(QueryNameTimeoutMs)) return null;
        return name;
    }

    private static string? QueryObjectNameGrowable(IntPtr h)
    {
        uint size = 4096;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                uint status = NtQueryObject(h, ObjectNameInformation, buf, size, out uint retLen);
                if (status == STATUS_INFO_LENGTH_MISMATCH || status == STATUS_BUFFER_OVERFLOW)
                {
                    size = Math.Max(retLen + 256, size * 2);
                    continue;
                }
                if (status != 0) return null;
                var us = Marshal.PtrToStructure<UNICODE_STRING>(buf);
                if (us.Length == 0 || us.Buffer == IntPtr.Zero) return null;
                return Marshal.PtrToStringUni(us.Buffer, us.Length / 2);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        return null;
    }

    private static string? TryGetDevicePrefix(string driveLetter) // "Z:" -> @"\Device\...\"
    {
        try
        {
            var sb = new System.Text.StringBuilder(261);
            if (QueryDosDeviceW(driveLetter, sb, (uint)sb.Capacity) != 0)
                return sb.ToString().TrimEnd('\\') + "\\";
        }
        catch { }
        return null;
    }

    private static string? TryGetUniversalName(string localPath) // "Z:\dir" -> @"\\server\share\dir"
    {
        try
        {
            uint size = 4096;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                int rc = WNetGetUniversalNameW(localPath, UNIVERSAL_NAME_INFO_LEVEL, buf, ref size);
                if (rc == ERROR_MORE_DATA)
                {
                    Marshal.FreeHGlobal(buf);
                    buf = Marshal.AllocHGlobal((int)size);
                    rc = WNetGetUniversalNameW(localPath, UNIVERSAL_NAME_INFO_LEVEL, buf, ref size);
                }
                if (rc != NO_ERROR) return null;
                IntPtr p = Marshal.ReadIntPtr(buf); // UNIVERSAL_NAME_INFO.lpUniversalName
                if (p == IntPtr.Zero) return null;
                return Marshal.PtrToStringUni(p)?.TrimEnd('\\');
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { return null; }
    }

    private static List<KeyValuePair<string, string>> BuildDosDeviceMap()
    {
        // @"\Device\HarddiskVolume4" -> "E:", longest device prefix first so that
        // e.g. HarddiskVolume10 never matches a HarddiskVolume1 entry.
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (char c = 'A'; c <= 'Z'; c++)
        {
            string letter = $"{c}:";
            var sb = new System.Text.StringBuilder(261);
            uint r = QueryDosDeviceW(letter, sb, (uint)sb.Capacity);
            if (r != 0) map[sb.ToString()] = letter;
        }
        return map.OrderByDescending(kv => kv.Key.Length).ToList();
    }

    private static string? TryGetProcessNameFromHandle(IntPtr src)
    {
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            if (QueryFullProcessImageNameW(src, 0, sb, ref size))
                return Path.GetFileName(sb.ToString());
        }
        catch { }
        return null;
    }

    private static string TryGetProcessName(uint pid)
    {
        try { return Process.GetProcessById((int)pid).ProcessName + ".exe"; }
        catch { return $"pid:{pid}"; }
    }

    private static string TryGetProcessUser(IntPtr srcProcess)
    {
        // Best effort "DOMAIN\User" via the already-open process handle; "" on failure.
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(srcProcess, TOKEN_QUERY, out token) || token == IntPtr.Zero)
                return "";
            GetTokenInformation(token, TokenUserClass, IntPtr.Zero, 0, out uint need);
            if (need == 0 || need > 4096) return "";
            IntPtr buf = Marshal.AllocHGlobal((int)need);
            try
            {
                if (!GetTokenInformation(token, TokenUserClass, buf, need, out _)) return "";
                var tu = Marshal.PtrToStructure<TOKEN_USER>(buf);
                if (tu.User.Sid == IntPtr.Zero) return "";
                byte[] sid = new byte[68]; // max SID size; LookupAccountSidW reads the real length
                Marshal.Copy(tu.User.Sid, sid, 0, 68);
                var name = new System.Text.StringBuilder(256);
                uint cchName = 256;
                var domain = new System.Text.StringBuilder(256);
                uint cchDomain = 256;
                if (!LookupAccountSidW(null, sid, name, ref cchName, domain, ref cchDomain, out _))
                    return "";
                return domain.Length == 0 ? name.ToString() : domain + "\\" + name;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { return ""; }
        finally { if (token != IntPtr.Zero) CloseHandle(token); }
    }
}
