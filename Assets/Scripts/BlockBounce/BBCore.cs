// BBCore.cs — Block Bounce game engine (pure C#, no UnityEngine dependency).
//
// This is a faithful port of the engine in the Claude Design prototype
// (game-screen.jsx). Keeping it Unity-free means it can be unit-tested on its
// own — which is exactly the "run automated engine tests" success criterion in
// the project's ACCESSFM plan.
//
// All coordinates here are in the prototype's "canvas pixel" space:
//   - origin top-left, x → right, y → DOWN, board is CW x CH pixels.
// The Unity layer (BlockBounceGame.cs) converts these to world space for drawing.

using System;
using System.Collections.Generic;

namespace BlockBounce
{
    /// <summary>A single placed/active cell. hp = hits remaining before it shatters.</summary>
    public class Block
    {
        public int hp;
        public int maxHp;
        public Block(int hp) { this.hp = hp; this.maxHp = hp; }
    }

    /// <summary>The falling piece: a square shape grid plus per-filled-cell HP values.</summary>
    public class Piece
    {
        public string type;
        public int[][] shape;   // square n x n, 1 = filled
        public int x, y;        // top-left grid position (y can be negative above board)
        public int[] hps;       // hp for each filled cell, in row-major fill order
    }

    public class Ball
    {
        public float x, y, vx, vy, r, life;
    }

    public class Spark
    {
        public float x, y, vx, vy, life, max, size;
        public RGB color;
    }

    /// <summary>Plain 0-255 colour triple so the core stays Unity-free.</summary>
    public struct RGB
    {
        public byte r, g, b;
        public RGB(byte r, byte g, byte b) { this.r = r; this.g = g; this.b = b; }
        public static RGB Hex(int rgb) => new RGB((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
    }

    public struct LevelDef
    {
        public string name;
        public int cell;     // pixel size of one grid cell
        public int hpLo, hpHi;
        public int bpr;      // balls earned per cleared row
        public int lines;    // rows to clear to finish the level
        public LevelDef(string name, int cell, int hpLo, int hpHi, int bpr, int lines)
        { this.name = name; this.cell = cell; this.hpLo = hpLo; this.hpHi = hpHi; this.bpr = bpr; this.lines = lines; }
    }

    public static class BBData
    {
        // Demo mode uses index 0 (Warm-up). Live mode walks all 10.
        public static readonly LevelDef[] Levels =
        {
            new LevelDef("Warm-up",     48,  1,  2,  3,  3),
            new LevelDef("Drift",       48,  1,  3,  4,  4),
            new LevelDef("Cascade",     40,  2,  4,  5,  5),
            new LevelDef("Tilt",        40,  2,  6,  6,  5),
            new LevelDef("Surge",       32,  3,  7,  7,  6),
            new LevelDef("Vortex",      32,  4,  9,  8,  7),
            new LevelDef("Crucible",    30,  5, 11,  9,  8),
            new LevelDef("Maelstrom",   24,  7, 14, 10,  9),
            new LevelDef("Cataclysm",   24,  9, 18, 11, 10),
            new LevelDef("Singularity", 20, 12, 24, 12, 12),
        };

        // Standard 7 tetrominoes as square grids (matches BB_SHAPES in the prototype).
        public static readonly Dictionary<string, int[][]> Shapes = new Dictionary<string, int[][]>
        {
            { "I", new[]{ new[]{0,0,0,0}, new[]{1,1,1,1}, new[]{0,0,0,0}, new[]{0,0,0,0} } },
            { "O", new[]{ new[]{1,1}, new[]{1,1} } },
            { "T", new[]{ new[]{0,1,0}, new[]{1,1,1}, new[]{0,0,0} } },
            { "S", new[]{ new[]{0,1,1}, new[]{1,1,0}, new[]{0,0,0} } },
            { "Z", new[]{ new[]{1,1,0}, new[]{0,1,1}, new[]{0,0,0} } },
            { "L", new[]{ new[]{0,0,1}, new[]{1,1,1}, new[]{0,0,0} } },
            { "J", new[]{ new[]{1,0,0}, new[]{1,1,1}, new[]{0,0,0} } },
        };
        public static readonly string[] Types = { "I", "O", "T", "S", "Z", "L", "J" };

        /// <summary>HP → (fill colour, number colour). Ported from bbHpColor.</summary>
        public static (RGB fill, RGB text) HpColor(int hp)
        {
            if (hp <= 2)  return (RGB.Hex(0x4CE0B5), RGB.Hex(0x0F5443));
            if (hp <= 4)  return (RGB.Hex(0x5BC0FF), RGB.Hex(0x093A56));
            if (hp <= 7)  return (RGB.Hex(0xFFD43B), RGB.Hex(0x5B4400));
            if (hp <= 10) return (RGB.Hex(0xFF8A65), RGB.Hex(0x5A2715));
            if (hp <= 14) return (RGB.Hex(0xFF5B6E), RGB.Hex(0x5E1622));
            if (hp <= 19) return (RGB.Hex(0x8A6BFF), RGB.Hex(0x241148));
            return (RGB.Hex(0x2B1F4D), RGB.Hex(0xFFFFFF));
        }

        public static readonly RGB SparkRowClear = RGB.Hex(0xFFD43B);

        // Fake leaderboard seed (ported from LB_BASE).
        public static readonly (string name, int score)[] LeaderboardBase =
        {
            ("PuzzlePilot", 4280), ("BlockAce", 3950), ("GridNova", 3620),
            ("TileTitan", 3140), ("ComboCat", 2880), ("LineSlayer", 2510),
            ("CubeQuokka", 2240), ("DropMage", 1980), ("PixelPenguin", 1720),
            ("BrickFox", 1450), ("Shapeshift", 1180), ("RushPanda", 960),
        };

        public static List<(string name, int score, bool me)> BuildLeaderboard(string meName, int meScore)
        {
            var list = new List<(string, int, bool)>();
            foreach (var e in LeaderboardBase) list.Add((e.name, e.score, false));
            if (!string.IsNullOrEmpty(meName)) list.Add((meName, meScore, true));
            list.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            if (list.Count > 10) list.RemoveRange(10, list.Count - 10);
            return list;
        }
    }

    /// <summary>
    /// Mutable game state + all engine logic. Phase strings mirror the prototype:
    /// start | play | aim | launching | paused | over | won | demoDone | levelUp.
    /// The Unity layer reads these fields each frame to render, and calls the
    /// public action methods (MoveLeft, Rotate, HardDrop, ChooseAim, ...) in response to input.
    /// </summary>
    public class BBState
    {
        public const int CW = 480, CH = 720;

        public int level, score, totalLines, levelLines;
        public Block[][] grid;
        public int cols, rows, cell, ox, oy;
        public Piece piece;
        public string nextType;

        public double dropEvery = 700, lastDropAt;
        public bool softDrop;

        public string phase = "start";
        public int queuedBalls;
        public readonly List<Ball> activeBalls = new List<Ball>();
        public readonly List<Spark> sparks = new List<Spark>();

        public float aimX = CW / 2f, aimY = CH * 0.4f;
        public float launcherX = CW / 2f;
        public double launchAngle = -Math.PI / 2;
        public string launchMode = "aim";
        public double launchInterval = 70, lastLaunchAt;

        public bool demoMode;
        public int demoTarget = 50;
        public string deployMode = null; // null | "aim" | "random"

        // Replaces JS setTimeout (used for the 0.5s pause before a level-up banner).
        double scheduleTimer = -1;
        Action scheduledAction;

        readonly Random rng = new Random();

        public LevelDef CurLevel => BBData.Levels[level];

        // ── lifecycle ────────────────────────────────────────────────────────
        public void NewGame(bool demo, int target)
        {
            demoMode = demo;
            demoTarget = target;
            level = 0; score = 0; totalLines = 0; levelLines = 0;
            piece = null; nextType = null;
            queuedBalls = 0;
            activeBalls.Clear(); sparks.Clear();
            softDrop = false; deployMode = null;
            scheduledAction = null; scheduleTimer = -1;
            SetupLevel(0);
            phase = "start";
        }

        void SetupLevel(int idx)
        {
            var L = BBData.Levels[idx];
            level = idx;
            cell = L.cell;
            cols = CW / L.cell;
            rows = CH / L.cell;
            ox = (CW - cols * L.cell) / 2;
            oy = CH - rows * L.cell;
            grid = new Block[rows][];
            for (int r = 0; r < rows; r++) grid[r] = new Block[cols];
            levelLines = 0;
            queuedBalls = 0;
            activeBalls.Clear();
            sparks.Clear();
            softDrop = false;
            launcherX = ox + (cols * cell) / 2f;
            dropEvery = Math.Max(360, 720 - idx * 38);
            nextType = RandType();
            SpawnPiece();
        }

        // ── helpers ──────────────────────────────────────────────────────────
        string RandType() => BBData.Types[rng.Next(BBData.Types.Length)];
        int RandHp() { var L = BBData.Levels[level]; return L.hpLo + rng.Next(L.hpHi - L.hpLo + 1); }

        static int[][] CloneShape(int[][] s)
        {
            var o = new int[s.Length][];
            for (int r = 0; r < s.Length; r++) o[r] = (int[])s[r].Clone();
            return o;
        }

        static int[][] Rotate(int[][] shape)
        {
            int n = shape.Length;
            var o = new int[n][];
            for (int i = 0; i < n; i++) o[i] = new int[n];
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                    o[c][n - 1 - r] = shape[r][c];
            return o;
        }

        static int FirstFilledRow(int[][] shape)
        {
            for (int r = 0; r < shape.Length; r++)
                for (int c = 0; c < shape[0].Length; c++)
                    if (shape[r][c] != 0) return r;
            return 0;
        }

        bool Collides(int[][] sh, int px, int py)
        {
            for (int r = 0; r < sh.Length; r++)
                for (int c = 0; c < sh[0].Length; c++)
                {
                    if (sh[r][c] == 0) continue;
                    int gx = px + c, gy = py + r;
                    if (gx < 0 || gx >= cols) return true;
                    if (gy >= rows) return true;
                    if (gy >= 0 && grid[gy][gx] != null) return true;
                }
            return false;
        }

        // ── piece flow ───────────────────────────────────────────────────────
        void SpawnPiece()
        {
            string type = nextType ?? RandType();
            var shape = CloneShape(BBData.Shapes[type]);
            var hps = new List<int>();
            for (int r = 0; r < shape.Length; r++)
                for (int c = 0; c < shape[0].Length; c++)
                    if (shape[r][c] != 0) hps.Add(RandHp());

            int x = (cols - shape[0].Length) / 2;
            int y = -FirstFilledRow(shape);
            piece = new Piece { type = type, shape = shape, x = x, y = y, hps = hps.ToArray() };
            nextType = RandType();
            if (Collides(shape, x, y)) { piece = null; phase = "over"; }
        }

        public void TryRotate()
        {
            if (phase != "play" || piece == null) return;
            var p = piece;
            if (p.type == "O") return;
            var rot = Rotate(p.shape);
            int n = p.shape.Length;

            // Remap HP to rotated positions (same transform the cells undergo).
            var map = new Dictionary<(int, int), int>();
            int k = 0;
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                    if (p.shape[r][c] != 0) { map[(c, n - 1 - r)] = p.hps[k]; k++; }

            var newHps = new List<int>();
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                    if (rot[r][c] != 0) newHps.Add(map[(r, c)]);

            int[] kicks = { 0, -1, 1, -2, 2 };
            foreach (int kick in kicks)
                if (!Collides(rot, p.x + kick, p.y))
                {
                    p.shape = rot; p.hps = newHps.ToArray(); p.x += kick;
                    return;
                }
        }

        bool Move(int dx, int dy)
        {
            if (piece == null) return false;
            if (!Collides(piece.shape, piece.x + dx, piece.y + dy)) { piece.x += dx; piece.y += dy; return true; }
            return false;
        }

        public void MoveLeft()  { if (phase == "play") Move(-1, 0); }
        public void MoveRight() { if (phase == "play") Move(1, 0); }
        public void SoftDrop(bool on) { softDrop = on; }

        public void HardDrop()
        {
            if (phase != "play" || piece == null) return;
            while (Move(0, 1)) score += 2;
            Lock();
        }

        void SoftTick(double now)
        {
            if (phase != "play" || piece == null) return;
            if (now - lastDropAt < (softDrop ? 50 : dropEvery)) return;
            lastDropAt = now;
            if (!Move(0, 1)) Lock();
            else if (softDrop) score += 1;
        }

        void Lock()
        {
            var p = piece;
            int k = 0;
            for (int r = 0; r < p.shape.Length; r++)
                for (int c = 0; c < p.shape[0].Length; c++)
                {
                    if (p.shape[r][c] == 0) continue;
                    int gx = p.x + c, gy = p.y + r;
                    int hp = p.hps[k++];
                    if (gy >= 0 && gy < rows && gx >= 0 && gx < cols) grid[gy][gx] = new Block(hp);
                }
            piece = null;

            var cleared = new List<int>();
            for (int r = 0; r < rows; r++)
            {
                bool full = true;
                for (int c = 0; c < cols; c++) if (grid[r][c] == null) { full = false; break; }
                if (full) cleared.Add(r);
            }
            if (cleared.Count > 0) OnCleared(cleared);
            else AfterPiece();
        }

        static readonly int[] LineScore = { 0, 100, 300, 600, 1000 };

        void OnCleared(List<int> rowsCleared)
        {
            var L = CurLevel;
            int ballsEarned = rowsCleared.Count * L.bpr;
            int lineScore = rowsCleared.Count < LineScore.Length ? LineScore[rowsCleared.Count] : 1500 + rowsCleared.Count * 200;
            score += lineScore;
            queuedBalls += ballsEarned;
            totalLines += rowsCleared.Count;
            levelLines += rowsCleared.Count;

            rowsCleared.Sort();
            foreach (int r in rowsCleared)
            {
                for (int c = 0; c < cols; c++)
                    Spark(ox + c * cell + cell / 2f, oy + r * cell + cell / 2f, BBData.SparkRowClear);
                // remove row r, insert empty at top
                for (int rr = r; rr > 0; rr--) grid[rr] = grid[rr - 1];
                grid[0] = new Block[cols];
            }

            if (demoMode)
            {
                if (score >= demoTarget) { phase = "demoDone"; return; }
            }
            else
            {
                if (levelLines >= L.lines) { Schedule(500, LevelUp); return; }
            }
            AfterPiece();
        }

        void AfterPiece()
        {
            if (demoMode && score >= demoTarget) { phase = "demoDone"; return; }
            if (queuedBalls > 0 && phase != "over")
            {
                phase = "aim";
                deployMode = null; // force the deploy-mode picker each aim round
            }
            else SpawnPiece();
        }

        // ── ball launching ───────────────────────────────────────────────────
        public void ChooseAim() { deployMode = "aim"; }      // wait for click
        public void ChooseRandom() { deployMode = "random"; StartLaunching("random"); }

        void StartLaunching(string mode)
        {
            launchMode = mode ?? "aim";
            phase = "launching";
            lastLaunchAt = Now() - launchInterval;
        }

        /// <summary>Called by the Unity layer on a left-click during aim mode.</summary>
        public void ClickLaunch()
        {
            if (phase == "aim" && deployMode == "aim") StartLaunching("aim");
        }

        void LaunchOne(double now)
        {
            if (queuedBalls <= 0) return;
            if (now - lastLaunchAt < launchInterval) return;
            lastLaunchAt = now;
            queuedBalls -= 1;
            float speed = 8.5f;
            double a;
            if (launchMode == "random")
                a = -Math.PI + 0.18 + rng.NextDouble() * (Math.PI - 0.36);
            else
                a = launchAngle;
            float originJitter = launchMode == "random" ? (float)((rng.NextDouble() - 0.5) * 14) : 0f;
            activeBalls.Add(new Ball
            {
                x = launcherX + originJitter, y = CH - 6,
                vx = (float)Math.Cos(a) * speed, vy = (float)Math.Sin(a) * speed,
                r = 5, life = 12000,
            });
        }

        void StepBalls(double dt)
        {
            float left = ox, right = ox + cols * cell, top = oy;
            for (int i = activeBalls.Count - 1; i >= 0; i--)
            {
                var b = activeBalls[i];
                b.life -= (float)dt;
                const int steps = 2;
                for (int k = 0; k < steps; k++)
                {
                    b.x += b.vx / steps; b.y += b.vy / steps;
                    if (b.x - b.r < left)  { b.x = left + b.r;  b.vx = Math.Abs(b.vx); }
                    if (b.x + b.r > right) { b.x = right - b.r; b.vx = -Math.Abs(b.vx); }
                    if (b.y - b.r < top)   { b.y = top + b.r;   b.vy = Math.Abs(b.vy); }
                    if (b.y - b.r > CH + 20) break;
                    CollideBlocks(b);
                }
                if (b.y - b.r > CH + 20 || b.life <= 0) activeBalls.RemoveAt(i);
            }

            if (phase == "launching" && activeBalls.Count == 0 && queuedBalls == 0)
            {
                if (demoMode && score >= demoTarget) { phase = "demoDone"; return; }
                if (!demoMode && levelLines >= CurLevel.lines) { LevelUp(); return; }
                SpawnPiece();
                phase = "play";
                lastDropAt = Now();
            }
        }

        void CollideBlocks(Ball b)
        {
            int gx = (int)Math.Floor((b.x - ox) / cell);
            int gy = (int)Math.Floor((b.y - oy) / cell);
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = gx + dx, y = gy + dy;
                    if (x < 0 || x >= cols || y < 0 || y >= rows) continue;
                    var blk = grid[y][x];
                    if (blk == null) continue;
                    float bx = ox + x * cell, by = oy + y * cell;
                    float cxp = Math.Max(bx, Math.Min(b.x, bx + cell));
                    float cyp = Math.Max(by, Math.Min(b.y, by + cell));
                    float ddx = b.x - cxp, ddy = b.y - cyp;
                    if (ddx * ddx + ddy * ddy < b.r * b.r)
                    {
                        float oxp = b.r - Math.Abs(ddx);
                        float oyp = b.r - Math.Abs(ddy);
                        if (ddx == 0 && ddy == 0) { b.vy = -b.vy; b.y -= 1; }
                        else if (oxp < oyp) { b.vx = ddx > 0 ? Math.Abs(b.vx) : -Math.Abs(b.vx); b.x += ddx > 0 ? oxp : -oxp; }
                        else { b.vy = ddy > 0 ? Math.Abs(b.vy) : -Math.Abs(b.vy); b.y += ddy > 0 ? oyp : -oyp; }
                        blk.hp -= 1;
                        score += 5;
                        if (blk.hp <= 0)
                        {
                            grid[y][x] = null;
                            score += 25;
                            Spark(bx + cell / 2f, by + cell / 2f, BBData.HpColor(blk.maxHp).fill);
                        }
                        return; // one hit per step
                    }
                }
            }
        }

        // ── particles ────────────────────────────────────────────────────────
        void Spark(float x, float y, RGB color)
        {
            int n = 6 + rng.Next(4);
            for (int i = 0; i < n; i++)
            {
                double a = rng.NextDouble() * Math.PI * 2;
                double sp = 1.5 + rng.NextDouble() * 2.5;
                sparks.Add(new Spark
                {
                    x = x, y = y,
                    vx = (float)(Math.Cos(a) * sp), vy = (float)(Math.Sin(a) * sp - 1),
                    life = (float)(500 + rng.NextDouble() * 300), max = 700,
                    color = color, size = (float)(2 + rng.NextDouble() * 3),
                });
            }
        }

        void StepSparks(double dt)
        {
            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                var p = sparks[i];
                p.x += p.vx; p.y += p.vy; p.vy += 0.15f;
                p.life -= (float)dt;
                if (p.life <= 0) sparks.RemoveAt(i);
            }
        }

        // ── level / pause / end states ───────────────────────────────────────
        public int PendingLevelUpIdx { get; private set; } = -1;

        void LevelUp()
        {
            if (level + 1 >= BBData.Levels.Length) { phase = "won"; return; }
            phase = "levelUp";
            score += (level + 1) * 500;
            PendingLevelUpIdx = level + 1;
        }

        /// <summary>Called by the Unity layer when the player taps "Begin Level N".</summary>
        public void BeginPendingLevel()
        {
            if (PendingLevelUpIdx < 0) return;
            SetupLevel(PendingLevelUpIdx);
            PendingLevelUpIdx = -1;
            phase = "play";
            lastDropAt = Now();
        }

        public void StartGame() { phase = "play"; lastDropAt = Now(); }

        public void TogglePause()
        {
            if (phase == "play" || phase == "aim") phase = "paused";
            else if (phase == "paused") Resume();
        }

        void Resume()
        {
            phase = (queuedBalls > 0 && piece == null) ? "aim" : "play";
            lastDropAt = Now();
        }

        // ── scheduling + clock ───────────────────────────────────────────────
        void Schedule(double ms, Action a) { scheduleTimer = ms; scheduledAction = a; }

        double clockMs;
        double Now() => clockMs;

        /// <summary>Advance the simulation. Called once per Unity frame with dt in ms.</summary>
        public void Step(double dtMs)
        {
            clockMs += dtMs;
            double now = clockMs;

            if (scheduledAction != null)
            {
                scheduleTimer -= dtMs;
                if (scheduleTimer <= 0) { var a = scheduledAction; scheduledAction = null; a(); }
            }

            if (phase == "aim") UpdateAimAngle();
            if (phase == "play") SoftTick(now);
            if (phase == "launching") LaunchOne(now);
            if (phase == "launching" || phase == "aim") StepBalls(dtMs);
            StepSparks(dtMs);
        }

        /// <summary>Clamp the mouse-aim into an upward firing angle (ported from bbDrawAim).</summary>
        void UpdateAimAngle()
        {
            float lx = launcherX, ly = CH - 8;
            double a = Math.Atan2(aimY - ly, aimX - lx);
            if (a > -0.10) a = -0.10;
            if (a < -Math.PI + 0.10) a = -Math.PI + 0.10;
            launchAngle = a;
        }

        public double LaunchAngle => launchAngle;

        // ── progress helpers for the HUD ─────────────────────────────────────
        public float GoalProgress()
        {
            if (demoMode) return Math.Min(100f, (score / (float)demoTarget) * 100f);
            return Math.Min(100f, (levelLines / (float)CurLevel.lines) * 100f);
        }

        public int BallsDisplay => activeBalls.Count + queuedBalls;
    }
}
