using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

internal static class NativePreallocated
{
    internal sealed record Acquisition(Exception Exception, string Symbol, string PdbIdentity);

    internal static Acquisition Acquire()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("The native symbol probe requires Windows x64 CoreCLR.");

        using var process = Process.GetCurrentProcess();
        var module = process.Modules.Cast<ProcessModule>().Single(m => string.Equals(m.ModuleName, "coreclr.dll", StringComparison.OrdinalIgnoreCase));
        using var stream = File.OpenRead(module.FileName);
        using var pe = new PEReader(stream);
        var entry = pe.ReadDebugDirectory().Single(e => e.Type == DebugDirectoryEntryType.CodeView);
        var codeView = pe.ReadCodeViewDebugDirectoryData(entry);
        var pdbName = Path.GetFileName(codeView.Path);
        var identity = codeView.Guid.ToString("N").ToUpperInvariant() + codeView.Age.ToString("X");
        var directory = Path.Combine(AppContext.BaseDirectory, "symbols", identity);
        var pdbPath = Path.Combine(directory, pdbName);
        Directory.CreateDirectory(directory);

        if (!File.Exists(pdbPath))
        {
            var uri = new Uri($"https://msdl.microsoft.com/download/symbols/{pdbName}/{identity}/{pdbName}");
            Console.Error.WriteLine($"Downloading matching symbols: {uri}");
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            using var response = client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            var temporaryPath = pdbPath + ".download";
            using (var output = File.Create(temporaryPath))
                response.Content.CopyToAsync(output).GetAwaiter().GetResult();
            File.Move(temporaryPath, pdbPath, true);
        }

        var handle = process.Handle;
        SymSetOptions(0x00000002 | 0x00000400 | 0x00000200 | 0x00080000);
        if (!SymInitialize(handle, directory, false))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SymInitialize");

        try
        {
            var moduleBase = (ulong)module.BaseAddress.ToInt64();
            if (SymLoadModuleEx(handle, IntPtr.Zero, module.FileName, "coreclr", moduleBase, (uint)module.ModuleMemorySize, IntPtr.Zero, 0) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SymLoadModuleEx");

            const string symbolName = "coreclr!g_pPreallocatedOutOfMemoryException";
            var info = new SymbolInfo { SizeOfStruct = 88, MaxNameLen = 1024, Name = new byte[1024] };
            if (!SymFromName(handle, symbolName, ref info))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"SymFromName({symbolName})");
            if (info.ModBase != moduleBase || info.Address < moduleBase || info.Address + 8 > moduleBase + (ulong)module.ModuleMemorySize)
                throw new InvalidOperationException("Resolved symbol is outside the loaded CoreCLR image.");

            var bytes = new byte[IntPtr.Size];
            if (!ReadProcessMemory(handle, (IntPtr)(long)info.Address, bytes, (nuint)bytes.Length, out var read) || read != (nuint)bytes.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ReadProcessMemory(global handle)");
            var exceptionHandle = (IntPtr)BitConverter.ToInt64(bytes);
            if (exceptionHandle == IntPtr.Zero || (exceptionHandle.ToInt64() & 7) != 0)
                throw new InvalidOperationException("The runtime exception handle is null or unaligned.");
            if (!ReadProcessMemory(handle, exceptionHandle, bytes, (nuint)bytes.Length, out read) || read != (nuint)bytes.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ReadProcessMemory(handle slot)");
            var exception = GCHandle.FromIntPtr(exceptionHandle).Target as Exception
                ?? throw new InvalidOperationException("The runtime handle did not reference an Exception.");
            return new Acquisition(exception, symbolName, identity);
        }
        finally
        {
            SymCleanup(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SymbolInfo
    {
        public uint SizeOfStruct;
        public uint TypeIndex;
        public ulong Reserved1;
        public ulong Reserved2;
        public uint Index;
        public uint Size;
        public ulong ModBase;
        public uint Flags;
        public ulong Value;
        public ulong Address;
        public uint Register;
        public uint Scope;
        public uint Tag;
        public uint NameLen;
        public uint MaxNameLen;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024)] public byte[] Name;
    }

    [DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SymInitialize(IntPtr process, string searchPath, [MarshalAs(UnmanagedType.Bool)] bool invadeProcess);

    [DllImport("dbghelp.dll")]
    private static extern uint SymSetOptions(uint options);

    [DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern ulong SymLoadModuleEx(IntPtr process, IntPtr file, string imageName, string moduleName, ulong baseOfDll, uint dllSize, IntPtr data, uint flags);

    [DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SymFromName(IntPtr process, string name, ref SymbolInfo info);

    [DllImport("dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SymCleanup(IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] buffer, nuint size, out nuint bytesRead);
}
