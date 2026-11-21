using System;
using System.Linq;
using Godot;
using GodotAgent;

namespace Starter;

/// <summary>
/// The game's side of godot-agent: what 'state' says, how board cells map to the screen ('at', 'drag'), and the
/// game's own commands. Add a command here for anything an agent playtester needs that a player cannot do quickly.
/// </summary>
public static class AgentHooks
{
    public static AgentServer Make(Root root)
    {
        var agent = new AgentServer { Name = "Agent" };
        agent.State = () =>
        {
            Game g = root.Game;
            string screen = root.Current?.GetType().Name ?? "none";
            if (g == null) return $"screen: {screen}";
            // Invariant: the agent reads 4.5, whatever the machine's locale writes.
            return FormattableString.Invariant($"screen: {screen}; at {g.X},{g.Y}; gems {g.Score}/{Game.Goal}; {g.Steps} steps; time {g.Time:0.0}s\n") +
                   $"gems at: {string.Join(" ", g.Gems.Select(c => $"{c % Game.W},{c / Game.W}"))}\n" + Board(g);
        };
        agent.CellToScreen = (x, y) => root.Current is GameScreen gs ? gs.CellCentre(x, y) : throw new System.Exception("not on the game screen");
        agent.OnQuit = () => { if (root.Game != null) Profile.SaveGame(root.Game); };
        agent.Command("new", "new [SEED]            (debug) a new game in the agent's slot, with a seed", async a =>
        {
            root.NewGame(1, a.Length > 1 ? (ulong)AgentServer.Int(a[1]) : 42);
            await agent.Frames(2);
            return $"new game, seed {root.Game.Seed}";
        });
        agent.Command("teleport", "teleport X Y          (debug) put the player on a cell", async a =>
        {
            Game g = root.Game ?? throw new System.Exception("no game");
            g.X = AgentServer.Int(a[1]); g.Y = AgentServer.Int(a[2]);
            await agent.Frames(1);
            return $"at {g.X},{g.Y}";
        });
        return agent;
    }

    /// <summary>The board in ASCII: # wall, * gem, @ the player.</summary>
    public static string Board(Game g)
    {
        var sb = new System.Text.StringBuilder();
        for (int y = 0; y < Game.H; y++)
        {
            for (int x = 0; x < Game.W; x++)
                sb.Append(x == g.X && y == g.Y ? '@' : g.Walls.Contains(y * Game.W + x) ? '#' : g.Gems.Contains(y * Game.W + x) ? '*' : '.');
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }
}
