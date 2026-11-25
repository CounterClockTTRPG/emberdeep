using System;
using System.Collections.Generic;
using System.Linq;

namespace Emberdeep;

/// <summary>Something standing on a floor: a model, and what it does (blocks, is opened, is used).</summary>
public sealed class Prop
{
    public int Id;
    /// <summary>The model in assets/models (null: the view draws it, e.g. the stairs).</summary>
    public string Model;
    /// <summary>What it is to the rules: deco, chest, waystone, down, up, stash, well, house, brazier.</summary>
    public string Kind = "deco";
    public int X, Y;
    /// <summary>Its footprint in cells (a house is 5 x 4), from X, Y.</summary>
    public int W = 1, H = 1;
    public double Rot;
    public bool Blocks;
    /// <summary>Size in metres the view fits the model to (the longest side, or the height for tall things).</summary>
    public double Size = 1;
}

/// <summary>Where a pack of monsters stands when the floor is first entered.</summary>
public sealed class Spawn
{
    public string Mon;
    public double X, Y;
    public bool Champion;
    /// <summary>A quest foe's title (Mother Wort), or null.</summary>
    public string Unique;
    public int Pack;
}

public sealed class Room
{
    public int X, Y, W, H;
    public int CX => X + W / 2;
    public int CY => Y + H / 2;
    public bool Has(int x, int y) => x >= X && y >= Y && x < X + W && y < Y + H;
}

/// <summary>
/// A floor's layout, made from the game's seed and the depth (so it is never saved: the same seed makes it again).
/// Cells are a metre square: 0 solid rock, 1 floor. Props stand on cells and may block them.
/// </summary>
public sealed class Floor
{
    public int Depth, W, H;
    public byte[] Cells;
    public bool[] Blocked;
    public List<Prop> Props = new();
    public List<Spawn> Spawns = new();
    public List<Room> Rooms = new();
    /// <summary>Where the hero arrives from above (and from the village's waystone).</summary>
    public (int x, int y) Start;
    public FloorDef Def => Data.Floor(Depth);
    public bool Village => Depth == 0;

    public bool In(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
    public bool Floor_(int x, int y) => In(x, y) && Cells[y * W + x] == 1;
    public bool Walk(int x, int y) => In(x, y) && Cells[y * W + x] == 1 && !Blocked[y * W + x];
    public bool Walk(double x, double y) => Walk((int)Math.Floor(x), (int)Math.Floor(y));

    public Prop Find(string kind) => Props.FirstOrDefault(p => p.Kind == kind);

    public static Floor Make(ulong seed, int depth) => depth <= 0 ? MakeVillage(seed) : MakeDungeon(seed, depth);

    private Floor(int depth, int w, int h)
    {
        Depth = depth; W = w; H = h;
        Cells = new byte[w * h];
        Blocked = new bool[w * h];
    }

    private Prop Add(string model, string kind, int x, int y, bool blocks, double size, double rot = 0, int w = 1, int h = 1)
    {
        var p = new Prop { Id = Props.Count + 1, Model = model, Kind = kind, X = x, Y = y, W = w, H = h, Blocks = blocks, Size = size, Rot = rot };
        Props.Add(p);
        if (blocks)
            for (int yy = y; yy < y + h; yy++)
                for (int xx = x; xx < x + w; xx++)
                    if (In(xx, yy)) Blocked[yy * W + xx] = true;
        return p;
    }

    private void Unblock(Prop p)
    {
        Props.Remove(p);
        for (int yy = p.Y; yy < p.Y + p.H; yy++)
            for (int xx = p.X; xx < p.X + p.W; xx++)
                if (In(xx, yy)) Blocked[yy * W + xx] = Props.Any(o => o.Blocks && xx >= o.X && yy >= o.Y && xx < o.X + o.W && yy < o.Y + o.H);
    }

    // ---- the village ------------------------------------------------------------------------------------------------

    public const int VillageSize = 44;

    /// <summary>Emberwick: a square of open ground walled by dead woods, cottages, the well, the waystone, the way down.</summary>
    private static Floor MakeVillage(ulong seed)
    {
        var f = new Floor(0, VillageSize, VillageSize);
        for (int y = 3; y < f.H - 3; y++)
            for (int x = 3; x < f.W - 3; x++)
                f.Cells[y * f.W + x] = 1;
        var r = new Rng(seed ^ 0xE3B0C442UL);
        int c = f.W / 2;
        // The mouth of the Emberdeep, at the top of the village, flanked by pillars and braziers.
        f.Add(null, "down", c - 1, 6, false, 2, 0, 2, 2);
        f.Add("pillar", "deco", c - 4, 6, true, 3);
        f.Add("pillar", "deco", c + 3, 6, true, 3);
        f.Add("brazier", "brazier", c - 3, 8, true, 1.1);
        f.Add("brazier", "brazier", c + 2, 8, true, 1.1);
        f.Add("waystone", "waystone", c - 1, c - 1, true, 2.2, 0, 2, 2);
        f.Add("well", "well", c + 5, c + 4, true, 2, 0, 2, 2);
        f.Add("chest", "stash", c - 4, c + 1, true, 0.9, Math.PI / 2);
        // Cottages around the square: the smith's, the healer's, the elder's, two more.
        f.Add("house", "house", 6, 12, true, 5, Math.PI / 2, 5, 5);
        f.Add("house", "house", 6, 26, true, 5, Math.PI / 2, 5, 5);
        f.Add("house", "house", f.W - 11, 12, true, 5, -Math.PI / 2, 5, 5);
        f.Add("house", "house", f.W - 11, 26, true, 5, -Math.PI / 2, 5, 5);
        f.Add("house", "house", c - 3, f.H - 10, true, 5, Math.PI, 5, 5);
        f.Add("brazier", "brazier", 12, 20, true, 1.1);
        f.Add("brazier", "brazier", f.W - 13, 20, true, 1.1);
        f.Add("barrel", "deco", 11, 17, true, 0.9);
        f.Add("barrel", "deco", 11, 18, true, 0.9);
        f.Add("tomb", "deco", f.W - 9, f.H - 8, true, 2.2, 0.3, 2, 1);
        // Dead trees: a ring at the edge and a few in the square, never on a path.
        for (int k = 0; k < 70; k++)
        {
            int x = r.Int(3, f.W - 3), y = r.Int(3, f.H - 3);
            bool edge = x < 6 || y < 5 || x >= f.W - 6 || y >= f.H - 6;
            if (!edge && !r.Chance(0.15)) continue;
            if (Math.Abs(x - c) < 6 && Math.Abs(y - c) < 12) continue;
            if (!f.Walk(x, y) || f.Props.Any(p => Math.Abs(p.X - x) < 2 && Math.Abs(p.Y - y) < 2)) continue;
            f.Add("tree", "deco", x, y, true, 4, r.Float() * Math.Tau);
        }
        f.Start = (c, c + 3);
        f.KeepConnected(f.Start);
        return f;
    }

    /// <summary>Where the village folk stand: the elder by the waystone, the healer and the smith by their doors.</summary>
    public static readonly (string who, double x, double y)[] VillageFolk =
    {
        ("elder", VillageSize / 2 + 2.5, VillageSize / 2 - 1.5),
        ("healer", 12.5, 29.5),
        ("smith", VillageSize - 12.5, 15.5),
    };

    // ---- the dungeon ------------------------------------------------------------------------------------------------

    private static Floor MakeDungeon(ulong seed, int depth)
    {
        var r = new Rng(seed * 31 + (ulong)depth * 0x9E3779B1UL);
        if (depth >= Data.Bottom) return MakeThrone(r);
        string theme = Data.Floor(depth).Theme;
        int size = 56 + Math.Min(depth, 8) * 2;
        var f = new Floor(depth, size, size);
        int want = r.Int(9, 14), corridor = theme == "ember" ? 3 : 2;
        for (int tries = 0; tries < 400 && f.Rooms.Count < want; tries++)
        {
            int w = r.Int(6, 14), h = r.Int(6, 14);
            int x = r.Int(2, size - w - 2), y = r.Int(2, size - h - 2);
            var room = new Room { X = x, Y = y, W = w, H = h };
            if (f.Rooms.Any(o => x < o.X + o.W + 3 && o.X < x + w + 3 && y < o.Y + o.H + 3 && o.Y < y + h + 3)) continue;
            f.Rooms.Add(room);
        }
        foreach (Room room in f.Rooms) f.Carve(room.X, room.Y, room.W, room.H);
        // Join the rooms: a spanning tree over the nearest pairs, and a couple of loops.
        var joined = new List<int> { 0 };
        var edges = new List<(int a, int b)>();
        while (joined.Count < f.Rooms.Count)
        {
            (int a, int b, double d) best = (-1, -1, double.MaxValue);
            foreach (int a in joined)
                for (int b = 0; b < f.Rooms.Count; b++)
                {
                    if (joined.Contains(b)) continue;
                    double d = Dist(f.Rooms[a], f.Rooms[b]);
                    if (d < best.d) best = (a, b, d);
                }
            joined.Add(best.b);
            edges.Add((best.a, best.b));
        }
        for (int k = 0; k < 2; k++)
        {
            int a = r.Int(0, f.Rooms.Count), b = r.Int(0, f.Rooms.Count);
            if (a != b) edges.Add((a, b));
        }
        foreach (var (a, b) in edges) f.Corridor(f.Rooms[a], f.Rooms[b], corridor, r);
        Room start = f.Rooms[0];
        f.Start = (start.CX, start.CY + 1);
        // The way down: the room farthest from the start by walking.
        int[] dist = f.Distances(f.Start);
        Room far = f.Rooms.OrderByDescending(o => dist[o.CY * f.W + o.CX]).First();
        f.Add(null, "up", start.CX - 1, start.CY - 2, false, 2, 0, 2, 1);
        f.Add(null, "down", far.CX - 1, far.CY - 1, false, 2, 0, 2, 2);
        f.Add("waystone", "waystone", start.X + 1, start.Y + 1, true, 2.2, 0, 2, 2);
        f.Furnish(theme, r, start, far);
        f.KeepConnected(f.Start);
        f.Populate(r, start, far);
        return f;
    }

    /// <summary>The last floor: an antechamber and Vorgath's hall, pillars down both sides, braziers everywhere.</summary>
    private static Floor MakeThrone(Rng r)
    {
        var f = new Floor(Data.Bottom, 40, 56);
        var hall = new Room { X = 8, Y = 4, W = 24, H = 30 };
        var ante = new Room { X = 14, Y = 40, W = 12, H = 12 };
        f.Rooms.Add(ante);
        f.Rooms.Add(hall);
        f.Carve(hall.X, hall.Y, hall.W, hall.H);
        f.Carve(ante.X, ante.Y, ante.W, ante.H);
        f.Carve(18, 33, 4, 8);
        f.Start = (ante.CX, ante.CY + 2);
        f.Add(null, "up", ante.CX - 1, ante.CY + 3, false, 2, 0, 2, 1);
        f.Add("waystone", "waystone", ante.X + 1, ante.Y + 1, true, 2.2, 0, 2, 2);
        for (int y = hall.Y + 3; y < hall.Y + hall.H - 2; y += 5)
        {
            f.Add("pillar", "deco", hall.X + 3, y, true, 3);
            f.Add("pillar", "deco", hall.X + hall.W - 4, y, true, 3);
            f.Add("brazier", "brazier", hall.X + 1, y + 2, true, 1.1);
            f.Add("brazier", "brazier", hall.X + hall.W - 2, y + 2, true, 1.1);
        }
        f.Add("tomb", "deco", hall.CX - 1, hall.Y + 2, true, 2.2, 0, 2, 1);
        f.Add("brazier", "brazier", ante.X + 1, ante.Y + ante.H - 2, true, 1.1);
        f.Add("brazier", "brazier", ante.X + ante.W - 2, ante.Y + ante.H - 2, true, 1.1);
        f.Spawns.Add(new Spawn { Mon = "vorgath", X = hall.CX + 0.5, Y = hall.Y + 6.5, Unique = "Vorgath the Ashen Warden", Pack = 1 });
        int pack = 2;
        foreach (var (x, y) in new[] { (hall.X + 6, hall.Y + 12), (hall.X + hall.W - 7, hall.Y + 12), (hall.CX, hall.Y + 20) })
        {
            for (int k = 0; k < 3; k++)
                f.Spawns.Add(new Spawn { Mon = k == 0 ? "knight" : "imp", X = x + r.Float() * 2, Y = y + r.Float() * 2, Pack = pack });
            pack++;
        }
        f.KeepConnected(f.Start);
        return f;
    }

    private static double Dist(Room a, Room b) => Math.Abs(a.CX - b.CX) + Math.Abs(a.CY - b.CY);

    private void Carve(int x, int y, int w, int h)
    {
        for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++)
                if (xx > 0 && yy > 0 && xx < W - 1 && yy < H - 1) Cells[yy * W + xx] = 1;
    }

    /// <summary>An L-shaped corridor of a width from one room's centre to the other's.</summary>
    private void Corridor(Room a, Room b, int width, Rng r)
    {
        int x0 = a.CX, y0 = a.CY, x1 = b.CX, y1 = b.CY;
        if (r.Chance(0.5))
        {
            Carve(Math.Min(x0, x1), y0, Math.Abs(x1 - x0) + width, width);
            Carve(x1, Math.Min(y0, y1), width, Math.Abs(y1 - y0) + width);
        }
        else
        {
            Carve(x0, Math.Min(y0, y1), width, Math.Abs(y1 - y0) + width);
            Carve(Math.Min(x0, x1), y1, Math.Abs(x1 - x0) + width, width);
        }
    }

    /// <summary>Is every cell of a rectangle open floor with nothing on it (and a free ring around it)?</summary>
    private bool Clear(int x, int y, int w, int h, int ring = 1)
    {
        for (int yy = y - ring; yy < y + h + ring; yy++)
            for (int xx = x - ring; xx < x + w + ring; xx++)
                if (!Walk(xx, yy) || Props.Any(p => xx >= p.X && yy >= p.Y && xx < p.X + p.W && yy < p.Y + p.H)) return false;
        return true;
    }

    /// <summary>Is a cell next to rock (a place for a brazier against a wall)?</summary>
    private bool ByWall(int x, int y) => !Floor_(x - 1, y) || !Floor_(x + 1, y) || !Floor_(x, y - 1) || !Floor_(x, y + 1);

    private void Furnish(string theme, Rng r, Room start, Room far)
    {
        foreach (Room room in Rooms)
        {
            int area = room.W * room.H;
            // Braziers against the walls: the light of the place.
            int braziers = theme == "ember" ? 3 : area > 70 ? 2 : 1;
            for (int k = 0, tries = 0; k < braziers && tries < 30; tries++)
            {
                int x = room.X + r.Int(0, room.W), y = room.Y + r.Int(0, room.H);
                if (!ByWall(x, y) || !Walk(x, y) || Props.Any(p => Math.Abs(p.X - x) < 3 && Math.Abs(p.Y - y) < 3)) continue;
                Add("brazier", "brazier", x, y, true, 1.1);
                k++;
            }
            if (room == start) continue;
            // Pillars in the big rooms, in two rows.
            if (room.W >= 10 && room.H >= 9 && r.Chance(theme == "barrow" ? 0.4 : 0.7))
                for (int x = room.X + 2; x < room.X + room.W - 2; x += 4)
                {
                    if (Clear(x, room.Y + 2, 1, 1)) Add("pillar", "deco", x, room.Y + 2, true, 3);
                    if (Clear(x, room.Y + room.H - 3, 1, 1)) Add("pillar", "deco", x, room.Y + room.H - 3, true, 3);
                }
            // Tombs in the barrow and the catacombs.
            int tombs = theme == "ember" ? 0 : area > 80 ? 2 : r.Chance(0.5) ? 1 : 0;
            for (int k = 0, tries = 0; k < tombs && tries < 20; tries++)
            {
                int x = room.X + r.Int(1, room.W - 3), y = room.Y + r.Int(1, room.H - 2);
                if (!Clear(x, y, 2, 1)) continue;
                Add("tomb", "deco", x, y, true, 2.2, r.Chance(0.5) ? 0 : Math.PI, 2, 1);
                k++;
            }
            // Barrels in the corners, now and then.
            if (r.Chance(0.4))
            {
                int x = r.Chance(0.5) ? room.X : room.X + room.W - 1, y = r.Chance(0.5) ? room.Y : room.Y + room.H - 1;
                if (Walk(x, y)) Add("barrel", "deco", x, y, true, 0.9, r.Float() * Math.Tau);
            }
            // A chest in about one room in four, more often where the stairs are.
            if (r.Chance(room == far ? 0.8 : 0.25))
                for (int tries = 0; tries < 15; tries++)
                {
                    int x = room.X + r.Int(1, room.W - 1), y = room.Y + r.Int(1, room.H - 1);
                    if (!Clear(x, y, 1, 1)) continue;
                    Add("chest", "chest", x, y, true, 0.9, r.Float() * Math.Tau);
                    break;
                }
        }
    }

    private void Populate(Rng r, Room start, Room far)
    {
        var kinds = Data.MonstersFor(Depth).ToList();
        int pack = 1;
        foreach (Room room in Rooms)
        {
            if (room == start) continue;
            int packs = room.W * room.H > 90 ? 2 : 1;
            if (room != far && r.Chance(0.15)) packs = 0;
            for (int k = 0; k < packs; k++)
            {
                MonDef m = kinds[r.Int(0, kinds.Count)];
                int n = r.Int(m.PackMin, m.PackMax + 1);
                bool champ = Depth >= 2 && r.Chance(0.1);
                double cx = room.X + 1 + r.Float() * (room.W - 2), cy = room.Y + 1 + r.Float() * (room.H - 2);
                for (int i = 0; i < n; i++)
                {
                    double x = cx + (r.Float() - 0.5) * 3, y = cy + (r.Float() - 0.5) * 3;
                    if (!Walk(x, y)) { x = cx; y = cy; }
                    if (!Walk(x, y)) continue;
                    // A champion pack: the leader and one more are champions, the rest follow them.
                    Spawns.Add(new Spawn { Mon = m.Id, X = x, Y = y, Champion = champ && i < 2, Pack = pack });
                }
                pack++;
            }
        }
        // A quest foe guards the way down on its floor, with a few of its kind.
        foreach (var u in Data.Uniques.Where(u => u.depth == Depth))
        {
            Spawns.Add(new Spawn { Mon = u.mon, X = far.CX + 0.5, Y = far.CY + 2.5, Unique = u.title, Pack = pack });
            for (int i = 0; i < 3; i++)
                Spawns.Add(new Spawn { Mon = u.mon == "hag" ? "hollow" : "cultist", X = far.CX - 1.5 + i * 1.5, Y = far.CY + 3.5, Pack = pack });
            pack++;
        }
        Spawns.RemoveAll(s => !Walk(s.X, s.Y));
    }

    /// <summary>Steps from a cell to every other cell (-1: cannot be reached), walking through open cells.</summary>
    public int[] Distances((int x, int y) from)
    {
        var d = new int[W * H];
        Array.Fill(d, -1);
        if (!Walk(from.x, from.y)) return d;
        var q = new Queue<int>();
        d[from.y * W + from.x] = 0;
        q.Enqueue(from.y * W + from.x);
        while (q.Count > 0)
        {
            int c = q.Dequeue(), x = c % W, y = c / W;
            foreach (var (dx, dy) in Steps4)
            {
                int nx = x + dx, ny = y + dy;
                if (!Walk(nx, ny) || d[ny * W + nx] >= 0) continue;
                d[ny * W + nx] = d[c] + 1;
                q.Enqueue(ny * W + nx);
            }
        }
        return d;
    }

    private static readonly (int, int)[] Steps4 = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>
    /// Take away any blocking prop that cuts the floor in two, so every open cell can be walked to from the start; a
    /// prop someone uses (chest, stairs, waystone) is left and its neighbours checked instead.
    /// </summary>
    private void KeepConnected((int x, int y) from)
    {
        for (int pass = 0; pass < 60; pass++)
        {
            int[] d = Distances(from);
            int lost = -1;
            for (int c = 0; c < W * H; c++) if (Cells[c] == 1 && !Blocked[c] && d[c] < 0) { lost = c; break; }
            if (lost < 0) return;
            int lx = lost % W, ly = lost / W;
            Prop nearest = Props.Where(p => p.Blocks && p.Kind is "deco" or "brazier")
                .OrderBy(p => Math.Abs(p.X - lx) + Math.Abs(p.Y - ly)).FirstOrDefault();
            if (nearest == null) return;
            Unblock(nearest);
        }
    }

    /// <summary>
    /// Is the line between two points clear of rock (props do not block sight)? Every cell the line touches is
    /// checked, both sides of a corner it passes exactly through, so a missile that flies it never meets rock.
    /// </summary>
    public bool Sight(double x0, double y0, double x1, double y1)
    {
        int x = (int)Math.Floor(x0), y = (int)Math.Floor(y0), ex = (int)Math.Floor(x1), ey = (int)Math.Floor(y1);
        double dx = x1 - x0, dy = y1 - y0;
        int sx = Math.Sign(dx), sy = Math.Sign(dy);
        double tdx = dx == 0 ? double.MaxValue : Math.Abs(1 / dx), tdy = dy == 0 ? double.MaxValue : Math.Abs(1 / dy);
        double tmx = dx > 0 ? (x + 1 - x0) / dx : dx < 0 ? (x0 - x) / -dx : double.MaxValue;
        double tmy = dy > 0 ? (y + 1 - y0) / dy : dy < 0 ? (y0 - y) / -dy : double.MaxValue;
        for (int guard = 0; guard < 4 * (W + H); guard++)
        {
            if (!Floor_(x, y)) return false;
            if (x == ex && y == ey) return true;
            if (Math.Abs(tmx - tmy) < 1e-9)
            {
                if (!Floor_(x + sx, y) || !Floor_(x, y + sy)) return false;
                x += sx; y += sy; tmx += tdx; tmy += tdy;
            }
            else if (tmx < tmy) { x += sx; tmx += tdx; }
            else { y += sy; tmy += tdy; }
            if (tmx > 1 + 1e-9 && tmy > 1 + 1e-9 && (x != ex || y != ey)) return Floor_(x, y);
        }
        return true;
    }

    // ---- finding a way ----------------------------------------------------------------------------------------------

    private static readonly (int dx, int dy, double cost)[] Steps8 =
        { (1, 0, 1), (-1, 0, 1), (0, 1, 1), (0, -1, 1), (1, 1, 1.414), (1, -1, 1.414), (-1, 1, 1.414), (-1, -1, 1.414) };

    /// <summary>
    /// A* over the cells, eight ways (no cutting corners), from one point to the nearest reachable cell to another.
    /// Returns cell centres to walk through, the first step first; empty when already there or no way at all.
    /// </summary>
    public List<(double x, double y)> Path(double fx, double fy, double tx, double ty, int budget = 6000)
    {
        int sx = (int)Math.Floor(fx), sy = (int)Math.Floor(fy), gx = (int)Math.Floor(tx), gy = (int)Math.Floor(ty);
        var result = new List<(double, double)>();
        if (!In(sx, sy) || !In(gx, gy)) return result;
        int start = sy * W + sx, goal = gy * W + gx;
        var came = new Dictionary<int, int>();
        var cost = new Dictionary<int, double> { [start] = 0 };
        var open = new PriorityQueue<int, double>();
        open.Enqueue(start, 0);
        int best = start;
        double bestH = H8(sx, sy, gx, gy);
        int seen = 0;
        while (open.Count > 0 && seen++ < budget)
        {
            int c = open.Dequeue();
            if (c == goal) { best = c; break; }
            int x = c % W, y = c / W;
            double h = H8(x, y, gx, gy);
            if (h < bestH) { bestH = h; best = c; }
            foreach (var (dx, dy, step) in Steps8)
            {
                int nx = x + dx, ny = y + dy;
                if (!Walk(nx, ny) && (nx != gx || ny != gy || !Floor_(nx, ny))) continue;
                if (dx != 0 && dy != 0 && (!Walk(x + dx, y) || !Walk(x, y + dy))) continue;
                int n = ny * W + nx;
                double nc = cost[c] + step;
                if (cost.TryGetValue(n, out double old) && old <= nc) continue;
                cost[n] = nc;
                came[n] = c;
                open.Enqueue(n, nc + H8(nx, ny, gx, gy));
            }
        }
        for (int c = best; c != start; c = came[c]) result.Add((c % W + 0.5, c / W + 0.5));
        result.Reverse();
        if (result.Count > 0 && best == goal) result[^1] = (tx, ty);
        return result;
    }

    private static double H8(int x, int y, int gx, int gy)
    {
        int dx = Math.Abs(x - gx), dy = Math.Abs(y - gy);
        return Math.Max(dx, dy) + 0.414 * Math.Min(dx, dy);
    }
}
