using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace AccpHost
{
    public static class Wire
    {
        public static bool InvalidControls(string text)
        {
            bool quoted=false, escape=false;
            foreach(char c in text)
            {
                if(c < 32 && (quoted || (c != '\t' && c != '\r' && c != '\n'))) return true;
                if(escape) escape=false;
                else if(c == '\\' && quoted) escape=true;
                else if(c == '"') quoted=!quoted;
            }
            return false;
        }
        public static string Ascii(string text)
        {
            var result = new StringBuilder(text.Length);
            bool quoted=false, escape=false;
            foreach (char c in text)
            {
                if(c > 127) result.Append("\\u").Append(((int)c).ToString("x4"));
                else if(!quoted && (c == '\t' || c == '\r')) result.Append(' ');
                else result.Append(c);
                if(escape) escape=false;
                else if(c == '\\' && quoted) escape=true;
                else if(c == '"') quoted=!quoted;
            }
            return result.ToString();
        }
    }
    public sealed class Job : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct Basic
        {
            public long ProcessTime, JobTime;
            public uint Flags;
            public UIntPtr MinWorkingSet, MaxWorkingSet;
            public uint ActiveProcesses;
            public UIntPtr Affinity;
            public uint Priority, Scheduling;
        }
        [StructLayout(LayoutKind.Sequential)] struct Io
        {
            public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes;
        }
        [StructLayout(LayoutKind.Sequential)] struct Extended
        {
            public Basic Basic;
            public Io Io;
            public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int info, ref Extended data, uint size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool QueryInformationJobObject(IntPtr job, int info, out Extended data, uint size, IntPtr returned);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] static extern bool TerminateJobObject(IntPtr job, uint code);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        IntPtr handle;
        readonly ConsoleCancelEventHandler cancelHandler;

        public Job(ulong bytes)
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            var data = new Extended { Basic = new Basic { Flags = 0x2200 }, JobMemory = new UIntPtr(bytes) };
            if (handle == IntPtr.Zero || !SetInformationJobObject(handle, 9, ref data, (uint)Marshal.SizeOf<Extended>()))
            {
                Dispose();
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            cancelHandler = (sender, args) => Terminate();
            Console.CancelKeyPress += cancelHandler;
        }
        public void Assign(Process process)
        {
            if (!AssignProcessToJobObject(handle, process.Handle))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        public ulong PeakMemory()
        {
            return QueryInformationJobObject(handle, 9, out var data, (uint)Marshal.SizeOf<Extended>(), IntPtr.Zero)
                ? data.PeakJobMemory.ToUInt64() : 0;
        }
        public void Terminate() { if (handle != IntPtr.Zero) TerminateJobObject(handle, 1); }
        public void Dispose()
        {
            if(cancelHandler != null) Console.CancelKeyPress -= cancelHandler;
            var old = Interlocked.Exchange(ref handle, IntPtr.Zero);
            if (old != IntPtr.Zero) CloseHandle(old);
        }
    }

    public sealed class Frame
    {
        public string Text = "";
        public string Sha256 = "";
        public bool Eof, TooLarge, InvalidUtf8;
    }
    public sealed class Frames
    {
        readonly Stream input;
        readonly byte[] buffer = new byte[8192];
        int at, count;
        public Frames(Stream input) { this.input = input; }
        public Frame Read(int cap, bool drain)
        {
            using var output = new MemoryStream();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            bool tooLarge = false;
            long length = 0;
            Frame Finish(bool eof)
            {
                var frame = new Frame { Eof = eof && length == 0, TooLarge = tooLarge,
                    Sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() };
                if (!tooLarge)
                {
                    try { frame.Text = new UTF8Encoding(false, true).GetString(output.ToArray()).TrimEnd('\r'); }
                    catch (DecoderFallbackException) { frame.InvalidUtf8 = true; }
                }
                return frame;
            }
            for (;;)
            {
                if (at == count)
                {
                    count = input.Read(buffer, 0, buffer.Length);
                    at = 0;
                    if (count == 0) return Finish(true);
                }
                int stop = at;
                while (stop < count && buffer[stop] != 10) stop++;
                int n = stop - at;
                hash.AppendData(buffer, at, n);
                length += n;
                if (length > cap)
                {
                    tooLarge = true;
                    if (!drain) return Finish(false);
                }
                if (!tooLarge) output.Write(buffer, at, n);
                at = stop;
                if (at < count) { at++; return Finish(false); }
            }
        }
        public Task<Frame> ReadAsync(int cap) { return Task.Run(() => Read(cap, false)); }
    }

    public sealed class Result
    {
        public byte[] Output = Array.Empty<byte>(), Error = Array.Empty<byte>();
        public string Limit = "";
        public int ExitCode;
        public long Ms;
        public ulong PeakMemory;
    }

    public sealed class Spawn : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct Security { public uint Length; public IntPtr Descriptor; public int Inherit; }
        [StructLayout(LayoutKind.Sequential)] struct Startup
        {
            public uint Size;
            public IntPtr Reserved, Desktop, Title;
            public uint X, Y, Width, Height, CharsX, CharsY, Fill, Flags;
            public ushort Show, ReservedSize;
            public IntPtr ReservedBytes, Input, Output, Error;
        }
        [StructLayout(LayoutKind.Sequential)] struct StartupEx { public Startup Info; public IntPtr Attributes; }
        [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr Process, Thread; public uint Id, ThreadId; }
        [DllImport("kernel32.dll", SetLastError=true)] static extern bool CreatePipe(out IntPtr read, out IntPtr write, ref Security attributes, uint size);
        [DllImport("kernel32.dll", SetLastError=true)] static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
        [DllImport("kernel32.dll", SetLastError=true)] static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref UIntPtr size);
        [DllImport("kernel32.dll", SetLastError=true)] static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, UIntPtr attribute, IntPtr value, UIntPtr size, IntPtr previous, IntPtr returned);
        [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupEx startup, out ProcessInfo process);
        [DllImport("kernel32.dll", SetLastError=true)] static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process, uint code);
        public Process Process;
        public Stream Input, Output, Error;
        static void Check(bool ok) { if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
        static string Quote(string value)
        {
            var text = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                text.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                slashes = 0; text.Append(c);
            }
            return text.Append('\\', slashes * 2).Append('"').ToString();
        }
        public static Spawn Start(string executable, string[] args, Job job)
        {
            var sa = new Security { Length = (uint)Marshal.SizeOf<Security>(), Inherit = 1 };
            IntPtr ir=IntPtr.Zero, iw=IntPtr.Zero, or=IntPtr.Zero, ow=IntPtr.Zero, er=IntPtr.Zero, ew=IntPtr.Zero;
            IntPtr attributes=IntPtr.Zero, handles=IntPtr.Zero;
            ProcessInfo info = default;
            Spawn child = null;
            try
            {
                Check(CreatePipe(out ir, out iw, ref sa, 0));
                Check(CreatePipe(out or, out ow, ref sa, 0));
                Check(CreatePipe(out er, out ew, ref sa, 0));
                Check(SetHandleInformation(iw, 1, 0)); Check(SetHandleInformation(or, 1, 0)); Check(SetHandleInformation(er, 1, 0));
                UIntPtr size = UIntPtr.Zero;
                InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
                attributes = Marshal.AllocHGlobal((int)size.ToUInt64());
                Check(InitializeProcThreadAttributeList(attributes, 1, 0, ref size));
                handles = Marshal.AllocHGlobal(3 * IntPtr.Size);
                Marshal.WriteIntPtr(handles, 0, ir); Marshal.WriteIntPtr(handles, IntPtr.Size, ow); Marshal.WriteIntPtr(handles, 2 * IntPtr.Size, ew);
                Check(UpdateProcThreadAttribute(attributes, 0, new UIntPtr(0x20002), handles, new UIntPtr((uint)(3 * IntPtr.Size)), IntPtr.Zero, IntPtr.Zero));
                var startup = new StartupEx { Info = new Startup { Size=(uint)Marshal.SizeOf<StartupEx>(), Flags=0x100, Input=ir, Output=ow, Error=ew }, Attributes=attributes };
                var command = new StringBuilder(Quote(executable));
                foreach (var arg in args) command.Append(' ').Append(Quote(arg));
                Check(CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, true, 0x08080004, IntPtr.Zero, null, ref startup, out info));
                child = new Spawn { Process = Process.GetProcessById((int)info.Id) };
                job.Assign(child.Process);
                child.Input = new FileStream(new SafeFileHandle(iw, true), FileAccess.Write); iw=IntPtr.Zero;
                child.Output = new FileStream(new SafeFileHandle(or, true), FileAccess.Read); or=IntPtr.Zero;
                child.Error = new FileStream(new SafeFileHandle(er, true), FileAccess.Read); er=IntPtr.Zero;
                Check(ResumeThread(info.Thread) != uint.MaxValue);
                return child;
            }
            catch { if(info.Process != IntPtr.Zero) TerminateProcess(info.Process, 1); if (child != null) child.Dispose(); throw; }
            finally
            {
                foreach (var handle in new[] { ir, iw, or, ow, er, ew, info.Thread, info.Process }) if(handle != IntPtr.Zero) CloseHandle(handle);
                if(attributes != IntPtr.Zero) { DeleteProcThreadAttributeList(attributes); Marshal.FreeHGlobal(attributes); }
                if(handles != IntPtr.Zero) Marshal.FreeHGlobal(handles);
            }
        }
        public void Dispose() { Input?.Dispose(); Output?.Dispose(); Error?.Dispose(); Process?.Dispose(); }
    }

    public static class Child
    {
        public static Result Run(string executable, string[] args, byte[] input, int ms, int outputCap, ulong memory)
            => RunAsync(executable, args, input, ms, outputCap, memory).GetAwaiter().GetResult();

        static async Task<Result> RunAsync(string executable, string[] args, byte[] input, int ms, int outputCap, ulong memory)
        {
            var watch = Stopwatch.StartNew();
            using var job = new Job(memory);
            using var stdout = new MemoryStream();
            using var stderr = new MemoryStream();
            using var deadline = new CancellationTokenSource(ms);
            var result = new Result();
            long captured = 0;
            object gate = new object();
            void Stop(string reason)
            {
                lock (gate) { if (result.Limit == "") result.Limit = reason; }
                job.Terminate();
            }
            using var child = Spawn.Start(executable, args, job);
            var process = child.Process;
            using var registration = deadline.Token.Register(() => Stop("ms"));
            async Task Capture(Stream stream, MemoryStream destination)
            {
                byte[] chunk = new byte[8192];
                for (;;)
                {
                    int n = await stream.ReadAsync(chunk, 0, chunk.Length);
                    if (n == 0) break;
                    if (Interlocked.Add(ref captured, n) > outputCap) { Stop("output_bytes"); break; }
                    destination.Write(chunk, 0, n);
                }
            }
            async Task Feed()
            {
                try
                {
                    await child.Input.WriteAsync(input, 0, input.Length);
                    child.Input.Close();
                }
                catch (IOException) { }
            }
            var outTask = Capture(child.Output, stdout);
            var errTask = Capture(child.Error, stderr);
            var feedTask = Feed();
            await process.WaitForExitAsync();
            await Task.WhenAll(outTask, errTask, feedTask);
            result.Output = stdout.ToArray(); result.Error = stderr.ToArray();
            result.ExitCode = process.ExitCode; result.Ms = watch.ElapsedMilliseconds;
            result.PeakMemory = job.PeakMemory();
            return result;
        }
    }
}
