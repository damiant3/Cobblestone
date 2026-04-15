using Codex.Emit.X86_64;
using Xunit;
using Xunit.Abstractions;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Codex.Emit.Tests;

/// <summary>
/// Integration test: builds a minimal bare-metal ELF with hand-assembled
/// x86 code that writes "OK" to COM1 serial and halts, then boots it in
/// QEMU via WSL and verifies serial output. This validates that the ELF
/// structure produced by ElfWriter32 is actually bootable.
///
/// Skipped when WSL or QEMU is not available.
/// </summary>
public class ElfWriterBootTest
{
    private readonly ITestOutputHelper m_output;

    public ElfWriterBootTest(ITestOutputHelper output) => m_output = output;

    [Fact]
    public void BareMetal32_BootsInQemu_PrintsOK()
    {
        if (!HasWslQemu())
        {
            m_output.WriteLine("SKIP: WSL or qemu-system-x86_64 not available");
            return;
        }

        // Hand-assembled 32-bit x86 code that writes "OK\n" to COM1 and halts.
        // PVH enters in 32-bit protected mode. All immediates are 32-bit.
        //
        // 0x00: BA F8 03 00 00    mov edx, 0x000003F8  ; COM1 data port
        // 0x05: B0 4F             mov al, 'O'
        // 0x07: EE                out dx, al
        // 0x08: B0 4B             mov al, 'K'
        // 0x0A: EE                out dx, al
        // 0x0B: B0 0A             mov al, '\n'
        // 0x0D: EE                out dx, al
        // 0x0E: F4                hlt
        byte[] text = new byte[]
        {
            0xBA, 0xF8, 0x03, 0x00, 0x00,  // mov edx, 0x000003F8
            0xB0, 0x4F,                     // mov al, 'O'
            0xEE,                           // out dx, al
            0xB0, 0x4B,                     // mov al, 'K'
            0xEE,                           // out dx, al
            0xB0, 0x0A,                     // mov al, '\n'
            0xEE,                           // out dx, al
            0xF4,                           // hlt
        };

        // Build 32-bit bare-metal ELF with PVH note.
        // Entry offset = 0 (code starts at beginning of text section).
        byte[] elf = ElfWriter32.WriteExecutable(text, Array.Empty<byte>(), 0);
        m_output.WriteLine($"ELF size: {elf.Length} bytes");

        // Write to temp file and boot in QEMU via WSL
        string tempDir = Path.Combine(Path.GetTempPath(), $"codex_elf_boot_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string elfPath = Path.Combine(tempDir, "test.elf");
            File.WriteAllBytes(elfPath, elf);

            // Convert Windows path to WSL path
            string wslPath = WindowsToWslPath(elfPath);
            m_output.WriteLine($"WSL path: {wslPath}");

            // Boot in QEMU: -nographic sends serial to stdio, -no-reboot stops on halt
            string cmd = $"timeout 5 /usr/bin/qemu-system-x86_64 -kernel {wslPath} -nographic -no-reboot";
            (string? output, string? qemuStderr) = RunWsl(cmd);
            m_output.WriteLine($"QEMU output: [{output}]");
            if (!string.IsNullOrEmpty(qemuStderr))
            {
                m_output.WriteLine($"QEMU stderr: [{qemuStderr}]");
            }

            Assert.NotNull(output);

            // Look for "OK" in the output (may have QEMU boot messages before it)
            Assert.Contains("OK", output);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void BareMetal32_ElfIsValidPvhBinary()
    {
        // Structural validation: the minimal ELF should have correct format
        byte[] text = new byte[] { 0xF4 }; // just HLT
        byte[] elf = ElfWriter32.WriteExecutable(text, Array.Empty<byte>(), 0);

        // Verify it's a valid ELF
        Assert.True(elf.Length >= 52, "ELF too small for header");
        Assert.Equal(0x7F, elf[0]);
        Assert.Equal((byte)'E', elf[1]);

        // Verify PVH note is present with correct entry
        // Note at offset 116: namesz=4, descsz=4, type=18, "Xen\0", entry
        Assert.Equal(4u, BitConverter.ToUInt32(elf, 116));  // namesz
        Assert.Equal(18u, BitConverter.ToUInt32(elf, 124)); // type = XEN_ELFNOTE_PHYS32_ENTRY
        Assert.Equal((byte)'X', elf[128]);
    }

    static string WindowsToWslPath(string windowsPath)
    {
        // D:\foo\bar → /mnt/d/foo/bar
        string normalized = windowsPath.Replace('\\', '/');
        if (normalized.Length >= 2 && normalized[1] == ':')
        {
            char drive = char.ToLower(normalized[0]);
            return $"/mnt/{drive}{normalized[2..]}";
        }
        return normalized;
    }

    static (string?, string?) RunWsl(string command)
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
            if (proc is null)
            {
                return (null, null);
            }

            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(10_000);
            return (stdout, stderr);
        }
        catch { return (null, null); }
    }

    static bool HasWslQemu()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        try
        {
            (string? result, _) = RunWsl("which qemu-system-x86_64 2>/dev/null && echo FOUND");
            return result?.Contains("FOUND") == true;
        }
        catch { return false; }
    }
}
