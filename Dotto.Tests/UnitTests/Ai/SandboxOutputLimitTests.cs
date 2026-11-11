using System.Diagnostics;
using System.Text.RegularExpressions;
using Dotto.Ai.Sandbox;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

/// <summary>
/// Pins the output-size contract shared with server.py.
/// </summary>
public class SandboxOutputLimitTests
{
    // Copied to the test output directory by Dotto.Tests.csproj.
    private const string ServerScriptName = "sandbox-server.py";

    private static string ServerScriptPath => Path.Combine(AppContext.BaseDirectory, ServerScriptName);

    private static int ServerOutputCharCap()
    {
        var source = File.ReadAllText(ServerScriptPath);
        var match = Regex.Match(
            source,
            @"^\s*MAX_OUTPUT_CHARS\s*=\s*([\d_]+)\s*$",
            RegexOptions.Multiline);

        match.Success.ShouldBeTrue("server.py must define MAX_OUTPUT_CHARS as a number");
        return int.Parse(match.Groups[1].Value.Replace("_", ""));
    }

    private static string? FindPython()
    {
        foreach (var candidate in new[] { "python3", "python" })
        {
            try
            {
                using var probe = Process.Start(new ProcessStartInfo(candidate)
                {
                    ArgumentList = { "-c", "import sys; sys.exit(0 if sys.version_info >= (3, 8) else 1)" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                });
                probe!.WaitForExit(10_000);
                if (probe.ExitCode == 0)
                    return candidate;
            }
            catch (Exception)
            {
                // Not installed — try the next name.
            }
        }

        return null;
    }

    [Test]
    public void ClientBufferCoversWorstCaseEscaping()
    {
        var chars = ServerOutputCharCap();

        // Escaping must be accounted for: an astral char costs 12 bytes, so a
        // buffer sized from the character count alone would overflow.
        (12 * chars + 4096).ShouldBeLessThanOrEqualTo(
            (int)SandboxContainer.MaxBufferedResponseBytes);
        chars.ShouldBeLessThanOrEqualTo(10_000);
    }

    /// <summary>
    /// Exercises the real Python helpers against the same constants: worst-case
    /// escaping stays inside the client buffer, the tail buffer is memory-bounded
    /// under 10 MB of invalid UTF-8, and paths cannot be echoed unbounded.
    /// Ignored when no Python interpreter is available.
    /// </summary>
    [Test]
    public void ServerHelpersRespectOutputBounds()
    {
        var python = FindPython();
        if (python is null)
            Assert.Ignore("No python3/python interpreter available");

        const string script = """
            import importlib.util
            import json
            import sys

            spec = importlib.util.spec_from_file_location("sandbox_server", sys.argv[1])
            mod = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(mod)

            client_cap = int(sys.argv[2])

            # Worst case for the serialized body: astral characters become
            # surrogate-pair escapes (12 bytes each) in both fields.
            worst = "\U0001D11E" * 100000
            stdout, stderr = mod._cap_output(worst, worst)
            assert len(stdout) + len(stderr) <= mod.MAX_OUTPUT_CHARS
            body = json.dumps({"exitCode": 0, "stdout": stdout, "stderr": stderr}).encode("utf-8")
            assert len(body) <= 12 * mod.MAX_OUTPUT_CHARS + 4096, len(body)
            assert len(body) <= client_cap, (len(body), client_cap)

            # The budget is shared, not split 50/50: output that fits is kept
            # whole regardless of the other stream, and an oversized stdout does
            # not starve stderr (stderr always keeps at least half the budget).
            cap = mod.MAX_OUTPUT_CHARS
            s, e = mod._cap_output("s" * 9000, "e" * 200)
            assert (len(s), len(e)) == (9000, 200), (len(s), len(e))
            s, e = mod._cap_output("s" * 20000, "e" * 100)
            assert len(e) == 100, len(e)
            assert len(s) >= cap - 200, len(s)
            assert len(s) + len(e) <= cap, (len(s), len(e))
            s, e = mod._cap_output("s" * 100, "e" * 20000)
            assert len(s) == 100, len(s)
            assert len(e) >= cap - 200, len(e)
            assert len(s) + len(e) <= cap, (len(s), len(e))
            s, e = mod._cap_output("s" * 20000, "e" * 20000)
            assert len(s) + len(e) <= cap, (len(s), len(e))
            assert len(e) >= cap // 2 - 64, len(e)

            # Bounded tail memory: 10 MB of invalid UTF-8 in 64 KiB chunks.
            tail = mod._TailBuffer(4000)
            chunk = bytes(range(256)) * 4
            written = 0
            for _ in range(20000):
                tail.write(chunk)
                written += len(chunk)
                assert len(tail._data) <= 4000
            text = tail.text(100)
            assert isinstance(text, str)
            assert tail.dropped == written - 4000, (tail.dropped, written)
            assert len(text) <= 100
            assert "\ufffd" in text

            # The tail marker counts against its limit and names the unit it
            # actually counts (bytes).
            tiny = mod._TailBuffer(64)
            tiny.write(b"x" * 1000)
            assert len(tiny.text(5)) <= 5

            # Paths are length-bounded, so a response that echoes one cannot
            # grow past the client buffer.
            assert mod._resolve_path("/work/" + "a" * 5000, False) is None
            assert mod._resolve_path("/work/ok.txt", True) is not None

            print(len(body))
            """;

        using var process = Process.Start(new ProcessStartInfo(python)
        {
            ArgumentList =
            {
                "-c", script, ServerScriptPath,
                SandboxContainer.MaxBufferedResponseBytes.ToString(),
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        });

        process.ShouldNotBeNull();
        var stdout = process!.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(60_000).ShouldBeTrue("python helper script timed out");

        process.ExitCode.ShouldBe(0, stderr);

        var bodyBytes = int.Parse(stdout.Trim());
        bodyBytes.ShouldBeGreaterThan(0);
        bodyBytes.ShouldBeLessThanOrEqualTo((int)SandboxContainer.MaxBufferedResponseBytes);
    }
}
