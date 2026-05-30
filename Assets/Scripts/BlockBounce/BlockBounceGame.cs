// BlockBounceGame.cs — Unity layer for the Block Bounce engine (BBCore.cs).
//
// Responsibilities (everything visual / Unity-specific lives here):
//   • Auto-spawns itself when you press Play (no scene setup needed).
//   • Generates all sprites at runtime (rounded blocks, balls, sparks) — zero art assets.
//   • Reads the New Input System (the project's active backend) for keyboard + mouse.
//   • Draws the board in world space and the HUD / modals via IMGUI (OnGUI).
//
// Coordinate spaces:
//   • The engine works in "canvas pixels": origin top-left, y DOWN, board 480x720.
//   • Unity world uses 1 unit = 1 px, origin bottom-left. We flip Y: worldY = CH - canvasY.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using BlockBounce;

public class BlockBounceGame : MonoBehaviour
{
    // ── press-Play bootstrap ────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<BlockBounceGame>() != null) return;
        new GameObject("BlockBounceGame").AddComponent<BlockBounceGame>();
    }

    const int CW = BBState.CW, CH = BBState.CH;

    BBState s;
    Camera cam;

    // sprites (generated once)
    Sprite roundSprite, circleSprite;

    // persistent scenery
    SpriteRenderer boardBg, launcher, warnLine;

    // pools
    class CellVis { public GameObject blockGo; public SpriteRenderer sr; public GameObject textGo; public TextMesh tm; }
    class DotVis  { public GameObject go; public SpriteRenderer sr; }
    readonly List<CellVis> cellPool = new List<CellVis>();
    readonly List<DotVis>  dotPool  = new List<DotVis>();
    int cellCursor, dotCursor;
    Transform poolRoot;
    Font numberFont;

    // local profile (no account — just a local name for the leaderboard)
    string playerName;
    int best;
    bool editingName;

    // ── setup ───────────────────────────────────────────────────────────────
    void Awake()
    {
        playerName = PlayerPrefs.GetString("bb_name", "You");
        best = PlayerPrefs.GetInt("bb_best", 0);

        BuildSprites();
        SetupCamera();

        numberFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (numberFont == null) numberFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

        poolRoot = new GameObject("BB_Pools").transform;
        poolRoot.SetParent(transform, false);

        BuildScenery();

        s = new BBState();
        s.NewGame(true, 50); // start in demo mode, target 50
    }

    void SetupCamera()
    {
        cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
        }
        cam.orthographic = true;
        cam.orthographicSize = CH / 2f;          // shows 720px tall region
        cam.transform.position = new Vector3(CW / 2f, CH / 2f, -10f);
        cam.transform.rotation = Quaternion.identity;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Hex(0xFFF6EE);
        cam.nearClipPlane = 0.1f;
    }

    void BuildSprites()
    {
        roundSprite  = MakeSprite(RoundedRectTex(64, 0.16f));
        circleSprite = MakeSprite(CircleTex(64));
    }

    void BuildScenery()
    {
        boardBg  = MakeSR("BoardBg", roundSprite, Hex(0xFFFFFF, 0.45f), 0);
        warnLine = MakeSR("WarnLine", roundSprite, Hex(0xFF4E78, 0.45f), 1);
        launcher = MakeSR("Launcher", roundSprite, Hex(0x6B5BFF), 3);
    }

    SpriteRenderer MakeSR(string name, Sprite sp, Color col, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sp; sr.color = col; sr.sortingOrder = order;
        return sr;
    }

    // ── main loop ───────────────────────────────────────────────────────────
    void Update()
    {
        if (s == null) return;
        HandleInput();

        // feed mouse aim (engine clamps it to an upward angle)
        if (s.phase == "aim" && s.deployMode == "aim" && Mouse.current != null)
        {
            Vector3 w = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            s.aimX = w.x;
            s.aimY = CH - w.y;
        }

        double dt = Mathf.Min(48f, Time.deltaTime * 1000f);
        s.Step(dt);

        // persist best after an end state
        if ((s.phase == "demoDone" || s.phase == "over" || s.phase == "won") && s.score > best)
        {
            best = s.score;
            PlayerPrefs.SetInt("bb_best", best);
        }

        Render();
    }

    void HandleInput()
    {
        var k = Keyboard.current;
        if (k != null)
        {
            if (k.pKey.wasPressedThisFrame) s.TogglePause();
            if (s.phase == "play")
            {
                if (k.leftArrowKey.wasPressedThisFrame)  s.MoveLeft();
                if (k.rightArrowKey.wasPressedThisFrame) s.MoveRight();
                if (k.upArrowKey.wasPressedThisFrame)    s.TryRotate();
                if (k.spaceKey.wasPressedThisFrame)      s.HardDrop();
                s.SoftDrop(k.downArrowKey.isPressed);
            }
        }
        var m = Mouse.current;
        if (m != null && m.leftButton.wasPressedThisFrame && s.phase == "aim" && s.deployMode == "aim")
            s.ClickLaunch();
    }

    // ── rendering ───────────────────────────────────────────────────────────
    Vector3 W(float canvasX, float canvasY, float z = 0f) => new Vector3(canvasX, CH - canvasY, z);

    void Render()
    {
        cellCursor = 0; dotCursor = 0;

        float boardW = s.cols * s.cell, boardH = s.rows * s.cell;
        float boardCx = s.ox + boardW / 2f, boardCy = s.oy + boardH / 2f;
        boardBg.transform.position = W(boardCx, boardCy, 0.5f);
        boardBg.transform.localScale = new Vector3(boardW, boardH, 1f);

        // ceiling warning line (2 rows down)
        warnLine.enabled = true;
        warnLine.transform.position = W(s.ox + boardW / 2f, s.oy + s.cell * 2f, 0.4f);
        warnLine.transform.localScale = new Vector3(boardW - 8f, 2f, 1f);

        // placed blocks
        int pad = Mathf.Max(2, Mathf.FloorToInt(s.cell * 0.06f));
        float inner = s.cell - pad * 2;
        for (int r = 0; r < s.rows; r++)
            for (int c = 0; c < s.cols; c++)
            {
                var blk = s.grid[r][c];
                if (blk == null) continue;
                var col = BBData.HpColor(blk.hp);
                DrawCell(s.ox + c * s.cell + s.cell / 2f, s.oy + r * s.cell + s.cell / 2f,
                         inner, ToColor(col.fill), ToColor(col.text), blk.hp, 1f);
            }

        // ghost + active piece
        if (s.piece != null)
        {
            var p = s.piece;
            if (s.phase == "play")
            {
                int gy = p.y;
                while (!CollidesDown(p, gy + 1)) gy++;
                DrawPieceCells(p, gy, inner, ghost: true);
            }
            DrawPieceCells(p, p.y, inner, ghost: false);
        }

        // launcher
        bool showLauncher = s.phase == "aim" || s.phase == "launching" || s.queuedBalls > 0;
        launcher.enabled = showLauncher;
        if (showLauncher)
        {
            launcher.transform.position = W(s.launcherX, CH - 8, 0.3f);
            launcher.transform.localScale = new Vector3(32f, 14f, 1f);
        }

        // aim trajectory preview
        if (s.phase == "aim" && s.deployMode == "aim") DrawAim();

        // balls
        foreach (var b in s.activeBalls)
            DrawDot(b.x, b.y, b.r * 2f, Hex(0xFF4E78), 0.1f);

        // sparks
        foreach (var sp in s.sparks)
        {
            float a = Mathf.Clamp01(sp.life / sp.max);
            DrawDot(sp.x, sp.y, sp.size * 2f, ToColor(sp.color, a), -0.1f);
        }

        // hide unused pool objects
        for (int i = cellCursor; i < cellPool.Count; i++) { cellPool[i].blockGo.SetActive(false); cellPool[i].textGo.SetActive(false); }
        for (int i = dotCursor; i < dotPool.Count; i++) dotPool[i].go.SetActive(false);
    }

    bool CollidesDown(Piece p, int testY)
    {
        for (int r = 0; r < p.shape.Length; r++)
            for (int c = 0; c < p.shape[0].Length; c++)
            {
                if (p.shape[r][c] == 0) continue;
                int gx = p.x + c, gy = testY + r;
                if (gy >= s.rows) return true;
                if (gx < 0 || gx >= s.cols) return true;
                if (gy >= 0 && s.grid[gy][gx] != null) return true;
            }
        return false;
    }

    void DrawPieceCells(Piece p, int baseY, float inner, bool ghost)
    {
        int k = 0;
        for (int r = 0; r < p.shape.Length; r++)
            for (int c = 0; c < p.shape[0].Length; c++)
            {
                if (p.shape[r][c] == 0) continue;
                int hp = p.hps[k++];
                int gy = baseY + r, gx = p.x + c;
                if (gy < 0) continue;
                float cx = s.ox + gx * s.cell + s.cell / 2f;
                float cy = s.oy + gy * s.cell + s.cell / 2f;
                if (ghost)
                    DrawCell(cx, cy, inner, Hex(0xA89CC1, 0.22f), Color.clear, 0, 0.22f);
                else
                {
                    var col = BBData.HpColor(hp);
                    DrawCell(cx, cy, inner, ToColor(col.fill), ToColor(col.text), hp, 1f);
                }
            }
    }

    void DrawAim()
    {
        float lx = s.launcherX, ly = CH - 8;
        double a = s.LaunchAngle;
        float vx = (float)System.Math.Cos(a), vy = (float)System.Math.Sin(a);
        float sx = lx, sy = ly;
        float left = s.ox, right = s.ox + s.cols * s.cell, top = s.oy;
        const float step = 14f;
        for (int i = 0; i < 18; i++)
        {
            sx += vx * step; sy += vy * step;
            if (sx < left + 5)  { sx = left + 5;  vx = -vx; }
            if (sx > right - 5) { sx = right - 5; vx = -vx; }
            if (sy < top + 5)   { sy = top + 5;   vy = -vy; }
            if (sy > CH) break;
            float sz = Mathf.Max(1.4f, 3.4f - i * 0.12f);
            float alpha = Mathf.Max(0.12f, 0.6f - i * 0.03f);
            DrawDot(sx, sy, sz * 2f, Hex(0x2B1F4D, alpha), -0.05f);
        }
    }

    // ── pooled draw primitives ──────────────────────────────────────────────
    void DrawCell(float canvasCx, float canvasCy, float worldSize, Color fill, Color textCol, int number, float alpha)
    {
        var v = NextCell();
        v.blockGo.SetActive(true);
        v.sr.color = new Color(fill.r, fill.g, fill.b, fill.a * alpha);
        v.blockGo.transform.position = W(canvasCx, canvasCy, 0f);
        v.blockGo.transform.localScale = Vector3.one * worldSize;

        if (number > 0 && alpha > 0.5f && numberFont != null)
        {
            v.textGo.SetActive(true);
            string str = number.ToString();
            if (v.tm.text != str) v.tm.text = str;
            v.tm.color = textCol;
            // analytic fit: TextMesh height ≈ fontSize * characterSize * 0.1
            float natural = v.tm.fontSize * v.tm.characterSize * 0.1f;
            float scale = (worldSize * 0.55f) / Mathf.Max(0.001f, natural);
            v.textGo.transform.position = W(canvasCx, canvasCy, -0.2f);
            v.textGo.transform.localScale = Vector3.one * scale;
        }
        else v.textGo.SetActive(false);
    }

    void DrawDot(float canvasX, float canvasY, float worldDiameter, Color col, float z)
    {
        var d = NextDot();
        d.go.SetActive(true);
        d.sr.color = col;
        d.go.transform.position = W(canvasX, canvasY, z);
        d.go.transform.localScale = Vector3.one * worldDiameter;
    }

    CellVis NextCell()
    {
        if (cellCursor < cellPool.Count) return cellPool[cellCursor++];
        var blockGo = new GameObject("cell");
        blockGo.transform.SetParent(poolRoot, false);
        var sr = blockGo.AddComponent<SpriteRenderer>();
        sr.sprite = roundSprite; sr.sortingOrder = 2;

        var textGo = new GameObject("num");
        textGo.transform.SetParent(poolRoot, false);
        var tm = textGo.AddComponent<TextMesh>();
        tm.font = numberFont;
        tm.fontSize = 64; tm.characterSize = 1;
        tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
        var tmr = textGo.GetComponent<MeshRenderer>();
        if (numberFont != null) tmr.sharedMaterial = numberFont.material;
        tmr.sortingOrder = 4;

        var v = new CellVis { blockGo = blockGo, sr = sr, textGo = textGo, tm = tm };
        cellPool.Add(v); cellCursor++;
        return v;
    }

    DotVis NextDot()
    {
        if (dotCursor < dotPool.Count) return dotPool[dotCursor++];
        var go = new GameObject("dot");
        go.transform.SetParent(poolRoot, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = circleSprite; sr.sortingOrder = 6;
        var v = new DotVis { go = go, sr = sr };
        dotPool.Add(v); dotCursor++;
        return v;
    }

    // ── HUD / modals (IMGUI — functional MVP, easy to upgrade to uGUI later) ──
    GUIStyle stLabel, stValue, stTitle, stPill, stModalTitle, stBig, stBody, stLb;

    void EnsureStyles()
    {
        if (stLabel != null) return;
        stLabel = new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = Hex(0x6F628F) } };
        stValue = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, normal = { textColor = Hex(0x2B1F4D) } };
        stTitle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = Hex(0x2B1F4D) } };
        stPill  = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = Hex(0xFF4E78) } };
        stModalTitle = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Hex(0x2B1F4D) } };
        stBig   = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Hex(0xFF4E78) } };
        stBody  = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = Hex(0x6F628F) } };
        stLb    = new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = Hex(0x2B1F4D) } };
    }

    void OnGUI()
    {
        if (s == null) return;
        EnsureStyles();

        var L = s.CurLevel;

        // top-left status
        GUILayout.BeginArea(new Rect(16, 12, 520, 60));
        GUILayout.BeginHorizontal();
        GUILayout.Label("Block Bounce", stTitle);
        GUILayout.Space(10);
        GUILayout.Label(s.demoMode ? "DEMO ROUND" : "LIVE MATCH", stPill);
        GUILayout.Space(10);
        GUILayout.Label($"Level {(s.level + 1):00} · {L.name}", stLabel);
        GUILayout.EndHorizontal();
        GUILayout.EndArea();

        // stat row
        DrawStat(16,  74, "SCORE", s.score.ToString(), 0xFF4E78);
        DrawStat(146, 74, "BALLS", s.BallsDisplay.ToString(), 0x6B5BFF);
        DrawStat(276, 74, "ROWS",  s.totalLines.ToString(), 0x11A877);
        DrawStat(406, 74, "LEVEL", (s.level + 1).ToString("00"), 0x2B1F4D);

        // goal text
        string goal = s.demoMode
            ? $"Reach {s.demoTarget} pts · {Mathf.Min(s.score, s.demoTarget)}/{s.demoTarget}"
            : $"Clear {L.lines} rows · {s.levelLines}/{L.lines}";
        GUI.Label(new Rect(16, 132, 520, 20), "STAGE GOAL: " + goal, stLabel);

        // top-right buttons
        if (GUI.Button(new Rect(Screen.width - 180, 14, 80, 28), s.phase == "paused" ? "Resume" : "Pause")) s.TogglePause();
        if (GUI.Button(new Rect(Screen.width - 94, 14, 80, 28), "Quit")) { s.NewGame(true, 50); editingName = false; }

        // leaderboard (right side)
        DrawLeaderboard();

        // controls hint (bottom)
        GUI.Label(new Rect(16, Screen.height - 28, Screen.width - 32, 20),
            "←/→ move   ↑ rotate   ↓ soft drop   Space hard drop   P pause   Click launch", stLabel);

        // overlays
        switch (s.phase)
        {
            case "start":    DrawStartModal(); break;
            case "paused":   Modal("Paused", "", "Take a breath. The blocks will wait.", "Resume", () => s.TogglePause()); break;
            case "demoDone": DrawDemoDone(); break;
            case "levelUp":  DrawLevelUp(); break;
            case "won":      DrawWon(); break;
            case "over":     DrawOver(); break;
            case "aim":      if (s.deployMode == null) DrawDeployPicker(); else DrawAimHint(); break;
        }
    }

    void DrawStat(float x, float y, string label, string value, int color)
    {
        GUI.Box(new Rect(x, y, 120, 50), GUIContent.none);
        GUI.Label(new Rect(x + 10, y + 6, 110, 16), label, stLabel);
        var st = new GUIStyle(stValue) { normal = { textColor = Hex(color) } };
        GUI.Label(new Rect(x + 10, y + 18, 110, 30), value, st);
    }

    void DrawLeaderboard()
    {
        float x = Screen.width - 220, y = 90, w = 206;
        GUI.Box(new Rect(x, y, w, 360), GUIContent.none);
        GUI.Label(new Rect(x + 12, y + 8, w - 20, 20), "LEADERBOARD  ● LIVE", stLabel);
        var list = BBData.BuildLeaderboard(string.IsNullOrEmpty(playerName) ? "You" : playerName, Mathf.Max(best, s.score));
        for (int i = 0; i < list.Count; i++)
        {
            var row = list[i];
            float ry = y + 34 + i * 30;
            var style = new GUIStyle(stLb);
            if (row.me) style.normal.textColor = Hex(0xFF4E78);
            if (i < 3) style.fontStyle = FontStyle.Bold;
            GUI.Label(new Rect(x + 12, ry, 24, 24), (i + 1).ToString(), style);
            GUI.Label(new Rect(x + 40, ry, 110, 24), row.name + (row.me ? "  (YOU)" : ""), style);
            GUI.Label(new Rect(x + w - 64, ry, 60, 24), row.score.ToString("N0"), style);
        }
    }

    // modal frame helper
    Rect ModalRect() => new Rect(Screen.width / 2f - 190, Screen.height / 2f - 130, 380, 260);

    void ModalBackground()
    {
        var c = GUI.color; GUI.color = new Color(0.17f, 0.12f, 0.30f, 0.46f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = c;
    }

    void Modal(string title, string big, string body, string btn, System.Action onBtn)
    {
        ModalBackground();
        var r = ModalRect();
        GUI.Box(r, GUIContent.none);
        GUI.Label(new Rect(r.x, r.y + 24, r.width, 32), title, stModalTitle);
        if (!string.IsNullOrEmpty(big)) GUI.Label(new Rect(r.x, r.y + 64, r.width, 50), big, stBig);
        GUI.Label(new Rect(r.x + 24, r.y + 120, r.width - 48, 60), body, stBody);
        if (GUI.Button(new Rect(r.x + r.width / 2f - 90, r.y + r.height - 56, 180, 40), btn)) onBtn();
    }

    void DrawStartModal()
    {
        ModalBackground();
        var r = ModalRect();
        GUI.Box(r, GUIContent.none);
        GUI.Label(new Rect(r.x, r.y + 20, r.width, 32), "Block Bounce", stModalTitle);
        GUI.Label(new Rect(r.x + 24, r.y + 60, r.width - 48, 44),
            s.demoMode ? $"Drop pieces, mint balls, shatter the stack. Reach {s.demoTarget} points to unlock the live match."
                       : "10 levels, escalating tier. Every shatter counts.", stBody);
        GUI.Label(new Rect(r.x + 24, r.y + 118, 120, 20), "Player name:", stLabel);
        playerName = GUI.TextField(new Rect(r.x + 24, r.y + 138, r.width - 48, 28), playerName, 16);
        if (GUI.Button(new Rect(r.x + r.width / 2f - 90, r.y + r.height - 52, 180, 40), s.demoMode ? "Start Demo Round →" : "Start Live Match →"))
        {
            PlayerPrefs.SetString("bb_name", playerName);
            s.StartGame();
        }
    }

    void DrawDemoDone()
    {
        ModalBackground();
        var r = ModalRect();
        GUI.Box(r, GUIContent.none);
        GUI.Label(new Rect(r.x, r.y + 18, r.width, 28), "Nice — demo cleared!", stModalTitle);
        GUI.Label(new Rect(r.x, r.y + 52, r.width, 48), s.score.ToString(), stBig);
        GUI.Label(new Rect(r.x + 24, r.y + 108, r.width - 48, 40), $"You hit {s.demoTarget} points. Ready to take it live across 10 levels?", stBody);
        if (GUI.Button(new Rect(r.x + 30, r.y + r.height - 52, 150, 40), "Play Again")) { s.NewGame(true, 50); s.StartGame(); }
        if (GUI.Button(new Rect(r.x + r.width - 180, r.y + r.height - 52, 150, 40), "Start Live →")) { s.NewGame(false, 50); s.StartGame(); }
    }

    void DrawLevelUp()
    {
        ModalBackground();
        var r = ModalRect();
        int nextIdx = s.PendingLevelUpIdx;
        var nl = nextIdx >= 0 ? BBData.Levels[nextIdx] : s.CurLevel;
        GUI.Box(r, GUIContent.none);
        GUI.Label(new Rect(r.x, r.y + 18, r.width, 28), $"Level {(s.level + 1):00} cleared", stModalTitle);
        GUI.Label(new Rect(r.x, r.y + 52, r.width, 48), "+" + ((s.level + 1) * 500), stBig);
        GUI.Label(new Rect(r.x + 24, r.y + 108, r.width - 48, 44),
            $"Up next: {nl.name}. Cells shrink to {nl.cell}px, HP {nl.hpLo}–{nl.hpHi}, {nl.bpr} balls per row.", stBody);
        if (GUI.Button(new Rect(r.x + r.width / 2f - 90, r.y + r.height - 52, 180, 40), $"Begin Level {nextIdx + 1} →")) s.BeginPendingLevel();
    }

    void DrawWon()
    {
        ModalBackground();
        var r = ModalRect();
        GUI.Box(r, GUIContent.none);
        GUI.Label(new Rect(r.x, r.y + 18, r.width, 28), "Singularity cleared", stModalTitle);
        GUI.Label(new Rect(r.x, r.y + 52, r.width, 48), s.score.ToString(), stBig);
        GUI.Label(new Rect(r.x + 24, r.y + 108, r.width - 48, 44), $"You cleared all 10 levels and {s.totalLines} total rows.", stBody);
        if (GUI.Button(new Rect(r.x + 30, r.y + r.height - 52, 150, 40), "Quit")) s.NewGame(true, 50);
        if (GUI.Button(new Rect(r.x + r.width - 180, r.y + r.height - 52, 150, 40), "Play Again")) { s.NewGame(false, 50); s.StartGame(); }
    }

    void DrawOver()
    {
        ModalBackground();
        var r = ModalRect();
        GUI.Box(r, GUIContent.none);
        GUI.Label(new Rect(r.x, r.y + 18, r.width, 28), "Stack overflow", stModalTitle);
        GUI.Label(new Rect(r.x, r.y + 52, r.width, 48), s.score.ToString(), stBig);
        GUI.Label(new Rect(r.x + 24, r.y + 108, r.width - 48, 44), $"The blocks reached the ceiling. You cleared {s.totalLines} row{(s.totalLines == 1 ? "" : "s")}.", stBody);
        if (GUI.Button(new Rect(r.x + 30, r.y + r.height - 52, 150, 40), "Quit")) s.NewGame(true, 50);
        if (GUI.Button(new Rect(r.x + r.width - 180, r.y + r.height - 52, 150, 40), s.demoMode ? "Play Again" : "From Level 1")) { s.NewGame(s.demoMode, 50); s.StartGame(); }
    }

    void DrawDeployPicker()
    {
        ModalBackground();
        var r = new Rect(Screen.width / 2f - 190, Screen.height / 2f - 110, 380, 220);
        GUI.Box(r, GUIContent.none);
        GUI.Label(new Rect(r.x, r.y + 16, r.width, 28), "Deploy your balls", stModalTitle);
        GUI.Label(new Rect(r.x + 24, r.y + 50, r.width - 48, 24), $"{s.queuedBalls} balls ready · pick how to launch", stBody);
        if (GUI.Button(new Rect(r.x + 28, r.y + 96, 150, 90), "Aim & Shoot\n\nMouse + click")) s.ChooseAim();
        if (GUI.Button(new Rect(r.x + r.width - 178, r.y + 96, 150, 90), "Random Spray\n\nAuto-launch")) s.ChooseRandom();
    }

    void DrawAimHint()
    {
        GUI.Label(new Rect(Screen.width / 2f - 160, 100, 320, 24), "Move mouse to aim · Click to launch", stModalTitle);
        if (GUI.Button(new Rect(Screen.width / 2f - 90, Screen.height - 120, 180, 30), "↻ Switch to Random Spray")) s.ChooseRandom();
    }

    // ── texture / colour utilities ──────────────────────────────────────────
    static Sprite MakeSprite(Texture2D tex) =>
        Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);

    static Texture2D RoundedRectTex(int size, float radiusFrac)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        float rad = size * radiusFrac;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float nx = Mathf.Clamp(fx, rad, size - rad);
                float ny = Mathf.Clamp(fy, rad, size - rad);
                float dist = Mathf.Sqrt((fx - nx) * (fx - nx) + (fy - ny) * (fy - ny));
                float a = Mathf.Clamp01(rad - dist + 0.5f);
                px[y * size + x] = new Color(1, 1, 1, a);
            }
        tex.SetPixels(px); tex.Apply();
        return tex;
    }

    static Texture2D CircleTex(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        float c = size / 2f, rad = size / 2f;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dist = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                float a = Mathf.Clamp01(rad - dist + 0.5f);
                px[y * size + x] = new Color(1, 1, 1, a);
            }
        tex.SetPixels(px); tex.Apply();
        return tex;
    }

    static Color Hex(int rgb, float a = 1f) =>
        new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

    static Color ToColor(RGB c, float a = 1f) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, a);
}
