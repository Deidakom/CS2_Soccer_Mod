using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SoccerModMvp;

// The helper process that runs the CS:S ball (tools/css-vphysics-host/cssball.cpp): the CS:S
// server's own physics library with the CS:S stadium's collision, the players' physics shadows,
// the CS:S player move and the knife. One request per server tick over the helper's stdin, one
// answer over its stdout. A reader thread collects the answer lines, so a hung helper costs one
// timeout, not the server.
internal sealed class CssBallHost : IDisposable
{
    internal readonly record struct PlayerIn(
        int Slot, bool Alive, float X, float Y, float Z, float Vx, float Vy, float Vz, int Flags, int Buttons,
        float Yaw, float Pitch, float Duck, float MaxSpeed, float Lagged, bool Teleported, bool Knife);

    // Mask: 1 the helper moved him, 2 it changed his velocity
    internal readonly record struct PlayerOut(
        int Slot, int Mask, float X, float Y, float Z, float Vx, float Vy, float Vz, float VelocityModifier, float ViewZ, bool Touched);

    // What: 2 the world, 3 a player
    internal readonly record struct BallHit(int What, int Slot, float Speed, float DeltaTime, float X, float Y, float Z, float Nx, float Ny, float Nz);

    // What: 1 the ball, 2 the world, 3 a player
    internal readonly record struct KnifeSwing(int Slot, bool Stab, bool Hit, int What, float X, float Y, float Z, float NextPrimary, float NextSecondary);

    // End: +1 the goal at +y, -1 the goal at -y
    internal readonly record struct GoalTouch(int End, float X, float Y, float Z);

    internal sealed class StepResult
    {
        internal float X, Y, Z, Pitch, Yaw, Roll, Vx, Vy, Vz, Wx, Wy, Wz;
        internal bool Asleep;
        internal readonly List<PlayerOut> Players = new();
        internal readonly List<BallHit> Hits = new();
        internal readonly List<KnifeSwing> Swings = new();
        internal readonly List<GoalTouch> Goals = new();
    }

    private readonly Process _process;
    private readonly BlockingCollection<string> _lines = new();
    private readonly ConcurrentQueue<string> _errors = new();
    private readonly StringBuilder _request = new();
    private readonly Thread _reader;
    private bool _disposed;
    internal string Greeting { get; private set; } = "";
    internal string LastError { get; private set; } = "";
    internal bool Ready { get; private set; }
    internal bool Exited { get { try { return _process.HasExited; } catch { return true; } } }

    private CssBallHost(Process process)
    {
        _process = process;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "cssball-reader" };
    }

    // Starts the helper and returns at once; PollReady tells when it has loaded the map.
    internal static CssBallHost? Start(string executable, IEnumerable<string> arguments, string libraryPath, out string error)
    {
        error = "";
        try
        {
            var info = new ProcessStartInfo(executable)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? "",
            };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            // only the helper needs the CS:S libraries; the game server's own environment stays as it is
            info.Environment["LD_LIBRARY_PATH"] = libraryPath;
            info.Environment.Remove("LD_PRELOAD");
            var process = Process.Start(info);
            if (process is null) { error = "process did not start"; return null; }
            var host = new CssBallHost(process);
            process.ErrorDataReceived += (sender, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                host._errors.Enqueue(e.Data);
                while (host._errors.Count > 8 && host._errors.TryDequeue(out _)) { }
            };
            process.BeginErrorReadLine();
            host._reader.Start();
            return host;
        }
        catch (Exception e)
        {
            error = e.Message;
            return null;
        }
    }

    // True once the helper has said READY; false while it loads. A helper that failed sets LastError.
    internal bool PollReady()
    {
        if (Ready) return true;
        while (_lines.TryTake(out var line))
        {
            if (line.StartsWith("READY", StringComparison.Ordinal)) { Greeting = line; Ready = true; return true; }
            LastError = line;
        }
        if (Exited && LastError.Length == 0) LastError = "the helper exited: " + Errors();
        return false;
    }

    internal bool Failed => !Ready && (LastError.Length > 0 || Exited);

    internal string Errors() => string.Join(" | ", _errors);

    private void ReadLoop()
    {
        try
        {
            string? line;
            while ((line = _process.StandardOutput.ReadLine()) is not null) _lines.Add(line);
        }
        catch
        {
            // the process went away; Step notices through the timeout
        }
    }

    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    // spin: world axes, degrees per second
    internal void SetBall(float x, float y, float z, float pitch, float yaw, float roll, float vx, float vy, float vz, float wx, float wy, float wz, bool asleep) =>
        _request.Append("B ").Append(F(x)).Append(' ').Append(F(y)).Append(' ').Append(F(z)).Append(' ').Append(F(pitch)).Append(' ').Append(F(yaw)).Append(' ').Append(F(roll)).Append(' ')
            .Append(F(vx)).Append(' ').Append(F(vy)).Append(' ').Append(F(vz)).Append(' ').Append(F(wx)).Append(' ').Append(F(wy)).Append(' ').Append(F(wz)).Append(asleep ? " 1\n" : " 0\n");

    internal void Freeze(bool frozen) => _request.Append(frozen ? "F 1\n" : "F 0\n");

    internal void Push(float fx, float fy, float fz, float x, float y, float z) =>
        _request.Append("I ").Append(F(fx)).Append(' ').Append(F(fy)).Append(' ').Append(F(fz)).Append(' ').Append(F(x)).Append(' ').Append(F(y)).Append(' ').Append(F(z)).Append('\n');

    internal void Setting(string name, float value) => _request.Append("C ").Append(name).Append(' ').Append(F(value)).Append('\n');

    internal void Player(in PlayerIn p) =>
        _request.Append("P ").Append(p.Slot).Append(' ').Append(p.Alive ? 1 : 0).Append(' ')
            .Append(F(p.X)).Append(' ').Append(F(p.Y)).Append(' ').Append(F(p.Z)).Append(' ').Append(F(p.Vx)).Append(' ').Append(F(p.Vy)).Append(' ').Append(F(p.Vz)).Append(' ')
            .Append(p.Flags).Append(' ').Append(p.Buttons).Append(' ').Append(F(p.Yaw)).Append(' ').Append(F(p.Pitch)).Append(' ').Append(F(p.Duck)).Append(' ')
            .Append(F(p.MaxSpeed)).Append(' ').Append(F(p.Lagged)).Append(' ').Append(p.Teleported ? 1 : 0).Append(' ').Append(p.Knife ? 1 : 0).Append('\n');

    // One server tick: sends what was queued plus the step, waits for the answer.
    internal StepResult? Step(float dt, int timeoutMs)
    {
        _request.Append("S ").Append(F(dt)).Append('\n');
        var text = _request.ToString();
        _request.Clear();
        try
        {
            _process.StandardInput.Write(text);
            _process.StandardInput.Flush();
        }
        catch (Exception e)
        {
            LastError = "write: " + e.Message;
            return null;
        }
        var result = new StepResult();
        var sawBall = false;
        while (true)
        {
            if (!_lines.TryTake(out var line, timeoutMs))
            {
                LastError = Exited ? "the helper exited: " + Errors() : "no answer within " + timeoutMs + " ms";
                return null;
            }
            if (line == "E")
            {
                if (sawBall) return result;
                LastError = "answer without the ball";
                return null;
            }
            var t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 0) continue;
            try
            {
                float N(int i) => float.Parse(t[i], NumberStyles.Float, CultureInfo.InvariantCulture);
                int I(int i) => int.Parse(t[i], CultureInfo.InvariantCulture);
                switch (t[0])
                {
                    case "B" when t.Length >= 14:
                        result.X = N(1); result.Y = N(2); result.Z = N(3); result.Pitch = N(4); result.Yaw = N(5); result.Roll = N(6);
                        result.Vx = N(7); result.Vy = N(8); result.Vz = N(9); result.Wx = N(10); result.Wy = N(11); result.Wz = N(12);
                        result.Asleep = I(13) != 0;
                        sawBall = float.IsFinite(result.X) && float.IsFinite(result.Y) && float.IsFinite(result.Z)
                            && float.IsFinite(result.Pitch) && float.IsFinite(result.Yaw) && float.IsFinite(result.Roll)
                            && float.IsFinite(result.Vx) && float.IsFinite(result.Vy) && float.IsFinite(result.Vz);
                        if (!sawBall) { LastError = "the ball left the world: " + line; return null; }
                        break;
                    case "P" when t.Length >= 12:
                        result.Players.Add(new PlayerOut(I(1), I(2), N(3), N(4), N(5), N(6), N(7), N(8), N(9), N(10), I(11) != 0));
                        break;
                    case "H" when t.Length >= 11:
                        result.Hits.Add(new BallHit(I(1), I(2), N(3), N(4), N(5), N(6), N(7), N(8), N(9), N(10)));
                        break;
                    case "K" when t.Length >= 10:
                        result.Swings.Add(new KnifeSwing(I(1), I(2) != 0, I(3) != 0, I(4), N(5), N(6), N(7), N(8), N(9)));
                        break;
                    case "G" when t.Length >= 5:
                        result.Goals.Add(new GoalTouch(I(1), N(2), N(3), N(4)));
                        break;
                    case "ERROR":
                        LastError = line;
                        return null;
                }
            }
            catch (FormatException)
            {
                LastError = "unreadable answer: " + line;
                return null;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (!_process.HasExited)
            {
                try { _process.StandardInput.Write("Q\n"); _process.StandardInput.Flush(); } catch { }
                if (!_process.WaitForExit(300)) _process.Kill();
            }
        }
        catch
        {
            // already gone
        }
        try { _process.Dispose(); } catch { }
    }
}
