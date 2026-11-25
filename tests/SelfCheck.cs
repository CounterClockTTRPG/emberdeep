using System;
using System.Linq;
using System.Reflection;
using Godot;

namespace Emberdeep;

/// <summary>Marks a method of <see cref="SelfCheck"/> as a section of the self-check.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SectionAttribute : Attribute
{
    /// <summary>Slow sections run only when asked for by name (--only X).</summary>
    public bool OnlyWhenAsked;
}

/// <summary>
/// The headless self-check: <c>./play.sh --check</c> (or <c>--check --only X</c>). Every method marked [Section] in
/// this partial class is a section, run in file order; the checks inside use <see cref="Check"/> and <see cref="Eq"/>.
/// A section that throws counts as a failure with its stack trace. Exits 1 on any failure. Add sections in new
/// files: <c>public partial class SelfCheck { [Section] private void MyRule() { ... } }</c>.
/// </summary>
public partial class SelfCheck : Node
{
    private int _checks, _failures;
    private string _only;

    public override void _Ready()
    {
        string game = ProjectSettings.GetSetting("application/config/name").AsString();
        GD.Print($"{game.ToUpperInvariant()} self-check");
        ulong started = Time.GetTicksMsec();
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--only");
        if (at >= 0 && at + 1 < args.Length) _only = args[at + 1];
        var sections = GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Select(m => (m, s: m.GetCustomAttribute<SectionAttribute>())).Where(x => x.s != null).OrderBy(x => x.m.MetadataToken);
        int ran = 0;
        foreach (var (m, s) in sections)
        {
            bool named = _only != null && m.Name.Contains(_only, StringComparison.OrdinalIgnoreCase);
            if (_only != null ? !named : s.OnlyWhenAsked) continue;
            ran++;
            int before = _failures;
            ulong t = Time.GetTicksMsec();
            try { m.Invoke(this, null); }
            catch (TargetInvocationException e) { _failures++; GD.PrintErr($"  {m.Name}: threw {e.InnerException?.GetType().Name}: {e.InnerException?.Message}\n{e.InnerException?.StackTrace}"); }
            GD.Print($"  {(_failures == before ? "ok  " : "FAIL")} {m.Name} ({Time.GetTicksMsec() - t} ms)");
        }
        if (ran == 0) { GD.PrintErr($"no section matches '{_only}'"); _failures++; }
        GD.Print($"{_checks} checks, {_failures} failed, {(Time.GetTicksMsec() - started) / 1000.0:0.0} s");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    protected void Check(bool ok, string what)
    {
        _checks++;
        if (ok) return;
        _failures++;
        GD.PrintErr("    failed: " + what);
    }

    protected void Eq<T>(T actual, T expected, string what) => Check(Equals(actual, expected), $"{what}: expected {expected}, got {actual}");
}
