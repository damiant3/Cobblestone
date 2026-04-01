using Xunit;
using Xunit.Abstractions;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Codex.Bootstrap.Tests;

public class Phase3M1BootTest
{
    private readonly ITestOutputHelper m_output;

    public Phase3M1BootTest(ITestOutputHelper output) => m_output = output;

    [Fact]
    public void MainEquals42_BootsInQemu_Prints42()
    {
        if (!HasWslQemu())
        {
            m_output.WriteLine("SKIP: WSL or qemu-system-x86_64 not available");
            return;
        }

        var mainDef = new IRDef(
            "\u001a\u000f\u0011\u0012",
            new List<IRParam>(),
            new IntegerTy(),
            new IrIntLit(42));
        var module = new IRModule(
            new Name("\u000e\u000d\u0013\u000e"),
            new List<IRDef> { mainDef });

        List<long> elfBytes = Codex_Codex_Codex.x86_64_emit_module(module);
        byte[] elf = elfBytes.Select(b => (byte)b).ToArray();

        m_output.WriteLine($"ELF size: {elf.Length} bytes");

        string tempDir = Path.Combine(Path.GetTempPath(), $"codex_m31_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string elfPath = Path.Combine(tempDir, "test.elf");
            File.WriteAllBytes(elfPath, elf);

            string wslPath = WindowsToWslPath(elfPath);
            m_output.WriteLine($"WSL path: {wslPath}");

            string cmd = $"timeout 10 /usr/bin/qemu-system-x86_64 -kernel {wslPath} -nographic -no-reboot 2>/dev/null";
            string? output = RunWsl(cmd);
            m_output.WriteLine($"QEMU output: [{output}]");

            Assert.NotNull(output);
            Assert.Contains("42", output);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void MainEquals42_ProducesValidElf()
    {
        var mainDef = new IRDef(
            "\u001a\u000f\u0011\u0012",
            new List<IRParam>(),
            new IntegerTy(),
            new IrIntLit(42));
        var module = new IRModule(
            new Name("\u000e\u000d\u0013\u000e"),
            new List<IRDef> { mainDef });

        List<long> elfBytes = Codex_Codex_Codex.x86_64_emit_module(module);
        byte[] elf = elfBytes.Select(b => (byte)b).ToArray();

        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "codex_m31_debug.elf"), elf);

        Assert.True(elf.Length >= 52, $"ELF too small: {elf.Length} bytes");
        Assert.Equal(0x7F, elf[0]);
        Assert.Equal((byte)'E', elf[1]);
        Assert.Equal((byte)'L', elf[2]);
        Assert.Equal((byte)'F', elf[3]);

        // Multiboot header is in the text section, which starts at a
        // computed offset within the ELF file. Verify PVH note is present.
        // The PVH note name is "Xen\0" at a fixed position in the ELF.
        bool foundXen = false;
        for (int i = 0; i < elf.Length - 3; i++)
        {
            if (elf[i] == (byte)'X' && elf[i + 1] == (byte)'e' && elf[i + 2] == (byte)'n' && elf[i + 3] == 0)
            {
                foundXen = true;
                break;
            }
        }
        Assert.True(foundXen, "PVH note with 'Xen' name not found in ELF");
    }

    static string WindowsToWslPath(string windowsPath)
    {
        string normalized = windowsPath.Replace('\\', '/');
        if (normalized.Length >= 2 && normalized[1] == ':')
        {
            char drive = char.ToLower(normalized[0]);
            return $"/mnt/{drive}{normalized[2..]}";
        }
        return normalized;
    }

    static string? RunWsl(string command)
    {
        try
        {
            using Process? proc = Process.Start(new ProcessStartInfo("wsl", $"bash -c \"{command}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (proc is null) return null;
            string stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(10_000);
            return stdout;
        }
        catch { return null; }
    }

    static bool HasWslQemu()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;
        try
        {
            string? result = RunWsl("which qemu-system-x86_64 2>/dev/null && echo FOUND");
            return result?.Contains("FOUND") == true;
        }
        catch { return false; }
    }
}
