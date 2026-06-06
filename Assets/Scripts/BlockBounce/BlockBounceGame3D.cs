// BlockBounceGame3D.cs — 3D render layer for the Block Bounce engine (BBCore.cs).
//
// Same gameplay/engine as the 2D version; rendering uses real 3D cube and sphere
// meshes, a perspective camera (tilted for depth), and a directional light.
// Spawned by BlockBounceLauncher when the player picks "Play 3D".
//
// Engine coordinates are canvas pixels (origin top-left, y DOWN). We scale by SC
// into world units and flip Y: worldY = (CH - canvasY) * SC.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using BlockBounce;

public class BlockBounceGame3D : MonoBehaviour
{
    const int CW = BBState.CW, CH = BBState.CH;
    const float SC = 0.1f;                 // world units per canvas pixel

    BBState s;
    Camera cam;
    Light sun;

    Material litMat;
    MaterialPropertyBlock mpb;
    static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    static readonly int ColorID     = Shader.PropertyToID("_Color");

    Mesh cubeMesh, sphereMesh;

    class CubeVis { public GameObject go; public Renderer r; public GameObject textGo; public TextMesh tm; }
    class BallVis { public GameObject go; public Renderer r; }
    readonly List<CubeVis> cubePool = new List<CubeVis>();
    readonly List<BallVis> ballPool = new List<BallVis>();
    int cubeCursor, ballCursor;
    Transform poolRoot;

    GameObject boardBack, warn, launcherObj;
    Font numberFont;

    string playerName;
    int best;

    // ── setup ───────────────────────────────────────────────────────────────
    void Awake()
    {
        playerName = PlayerPrefs.GetString("bb_name", "You");
        best = PlayerPrefs.GetInt("bb_best", 0);

        mpb = new MaterialPropertyBlock();
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        litMat = new Material(sh);

        // borrow primitive meshes once
        var tmpCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cubeMesh = tmpCube.GetComponent<MeshFilter>().sharedMesh; Destroy(tmpCube);
        var tmpSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphereMesh = tmpSphere.GetComponent<MeshFilter>().sharedMesh; Destroy(tmpSphere);

        numberFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (numberFont == null) numberFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

        poolRoot = new GameObject("BB3D_Pools").transform;
        poolRoot.SetParent(transform, false);

        SetupCameraAndLight();
        BuildScenery();

        s = new BBState();
        s.NewGame(true, 50);

        LeaderboardService.Fetch();
    }

    string lastPhase = "";
    int softDownFrame = -10;
    float GW, GH;   // logical GUI size (Screen size / DPI scale, for mobile)

    void SetupCameraAndLight()
    {
        cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("BB Camera"); go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
        }
        foreach (var c in Camera.allCameras) if (c != cam) c.enabled = false;
        cam.orthographic = false;
        cam.fieldOfView = 36f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.10f, 0.08f, 0.18f);
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 500f;

        sun = new GameObject("BB Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = Color.white;
        sun.intensity = 1.1f;
        sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.55f);

        FitCamera();
    }

    Vector3 P(float canvasX, float canvasY, float z) => new Vector3(canvasX * SC, (CH - canvasY) * SC, z);

    void FitCamera()
    {
        if (cam == null || s == null) { if (cam != null) cam.transform.position = new Vector3(CW * SC / 2f, CH * SC / 2f, -CH * SC); return; }
        float boardW = s.cols * s.cell * SC;
        float boardH = s.rows * s.cell * SC;
        Vector3 center = P(s.ox + s.cols * s.cell / 2f, s.oy + s.rows * s.cell / 2f, 0f);
        float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        float halfV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float distV = (boardH / 2f) / halfV;
        float distH = (boardW / 2f) / (halfV * aspect);
        float dist = Mathf.Max(distV, distH) * 1.12f;
        cam.transform.position = center + new Vector3(0f, boardH * 0.10f, -dist);
        cam.transform.LookAt(center);
    }

    void BuildScenery()
    {
        boardBack   = MakeBox("BoardBack", new Color(0.16f, 0.13f, 0.28f));
        warn        = MakeBox("Warn", new Color(1f, 0.31f, 0.47f));
        launcherObj = MakeBox("Launcher", new Color(0.42f, 0.36f, 1f));
    }

    GameObject MakeBox(string name, Color col)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = cubeMesh;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = litMat;
        SetColor(mr, col);
        return go;
    }

    void SetColor(Renderer r, Color col)
    {
        r.GetPropertyBlock(mpb);
        mpb.SetColor(BaseColorID, col);
        mpb.SetColor(ColorID, col);
        r.SetPropertyBlock(mpb);
    }

    // ── loop ────────────────────────────────────────────────────────────────
    void Update()
    {
        if (s == null) return;
        FitCamera();
        HandleInput();

        if (s.phase == "aim" && s.deployMode == "aim" && Pointer.current != null)
        {
            // project pointer (mouse or touch) onto the board plane (z = 0)
            Ray ray = cam.ScreenPointToRay(Pointer.current.position.ReadValue());
            if (Mathf.Abs(ray.direction.z) > 1e-4f)
            {
                float t = -ray.origin.z / ray.direction.z;
                Vector3 hit = ray.origin + ray.direction * t;
                s.aimX = hit.x / SC;
                s.aimY = CH - hit.y / SC;
            }
        }

        double dt = Mathf.Min(48f, Time.deltaTime * 1000f);
        s.Step(dt);

        if ((s.phase == "demoDone" || s.phase == "over" || s.phase == "won") && s.score > best)
        {
            best = s.score; PlayerPrefs.SetInt("bb_best", best);
        }

        if (lastPhase != s.phase)
        {
            if (s.phase == "demoDone" || s.phase == "over" || s.phase == "won")
                LeaderboardService.Submit(string.IsNullOrEmpty(playerName) ? "You" : playerName, s.score, s.level + 1);
            lastPhase = s.phase;
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
            }
        }
        bool kSoft = k != null && k.downArrowKey.isPressed;
        bool tSoft = Time.frameCount - softDownFrame <= 1;
        if (s.phase == "play") s.SoftDrop(kSoft || tSoft);

        var pt = Pointer.current;
        if (pt != null && pt.press.wasReleasedThisFrame && s.phase == "aim" && s.deployMode == "aim")
            s.ClickLaunch();
    }

    // ── rendering ───────────────────────────────────────────────────────────
    void Render()
    {
        cubeCursor = 0; ballCursor = 0;

        float boardW = s.cols * s.cell * SC, boardH = s.rows * s.cell * SC;
        int pad = Mathf.Max(2, Mathf.FloorToInt(s.cell * 0.06f));
        float inner = (s.cell - pad * 2) * SC;
        float depth = inner * 0.9f;

        Vector3 boardCenter = P(s.ox + s.cols * s.cell / 2f, s.oy + s.rows * s.cell / 2f, 0f);
        boardBack.transform.position = boardCenter + new Vector3(0, 0, depth * 0.65f);
        boardBack.transform.localScale = new Vector3(boardW + 0.6f, boardH + 0.6f, depth * 0.3f);

        warn.transform.position = P(s.ox + boardW / SC / 2f, s.oy + s.cell * 2f, -depth * 0.4f);
        warn.transform.localScale = new Vector3(boardW - 0.2f, 0.12f, 0.12f);

        // placed blocks
        for (int r = 0; r < s.rows; r++)
            for (int c = 0; c < s.cols; c++)
            {
                var blk = s.grid[r][c];
                if (blk == null) continue;
                var col = BBData.HpColor(blk.hp);
                DrawCube(s.ox + c * s.cell + s.cell / 2f, s.oy + r * s.cell + s.cell / 2f, inner, depth, ToColor(col.fill), ToColor(col.text), blk.hp);
            }

        // ghost + active piece
        if (s.piece != null)
        {
            if (s.phase == "play")
            {
                int gy = s.piece.y;
                while (!CollidesDown(s.piece, gy + 1)) gy++;
                DrawPiece(s.piece, gy, inner, depth, ghost: true);
            }
            DrawPiece(s.piece, s.piece.y, inner, depth, ghost: false);
        }

        // launcher
        bool showL = s.phase == "aim" || s.phase == "launching" || s.queuedBalls > 0;
        launcherObj.SetActive(showL);
        if (showL)
        {
            launcherObj.transform.position = P(s.launcherX, CH - 8, -depth * 0.3f);
            launcherObj.transform.localScale = new Vector3(3.2f, 1.4f, 1.2f);
        }

        // aim preview
        if (s.phase == "aim" && s.deployMode == "aim") DrawAim();

        // balls
        foreach (var b in s.activeBalls)
            DrawBall(b.x, b.y, b.r * 2f * SC, new Color(1f, 0.31f, 0.47f));

        // sparks
        foreach (var sp in s.sparks)
        {
            float a = Mathf.Clamp01(sp.life / sp.max);
            var col = ToColor(sp.color); col.a = a;
            DrawBall(sp.x, sp.y, sp.size * 1.6f * SC, col);
        }

        for (int i = cubeCursor; i < cubePool.Count; i++) { cubePool[i].go.SetActive(false); cubePool[i].textGo.SetActive(false); }
        for (int i = ballCursor; i < ballPool.Count; i++) ballPool[i].go.SetActive(false);
    }

    bool CollidesDown(Piece p, int testY)
    {
        for (int r = 0; r < p.shape.Length; r++)
            for (int c = 0; c < p.shape[0].Length; c++)
            {
                if (p.shape[r][c] == 0) continue;
                int gx = p.x + c, gy = testY + r;
                if (gy >= s.rows || gx < 0 || gx >= s.cols) return true;
                if (gy >= 0 && s.grid[gy][gx] != null) return true;
            }
        return false;
    }

    void DrawPiece(Piece p, int baseY, float inner, float depth, bool ghost)
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
                    DrawCube(cx, cy, inner * 0.96f, depth * 0.5f, new Color(0.66f, 0.61f, 0.76f, 1f), Color.clear, 0);
                else
                {
                    var col = BBData.HpColor(hp);
                    DrawCube(cx, cy, inner, depth, ToColor(col.fill), ToColor(col.text), hp);
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
        for (int i = 0; i < 16; i++)
        {
            sx += vx * step; sy += vy * step;
            if (sx < left + 5)  { sx = left + 5;  vx = -vx; }
            if (sx > right - 5) { sx = right - 5; vx = -vx; }
            if (sy < top + 5)   { sy = top + 5;   vy = -vy; }
            if (sy > CH) break;
            DrawBall(sx, sy, 0.45f, new Color(0.17f, 0.12f, 0.30f, 0.7f));
        }
    }

    void DrawCube(float canvasCx, float canvasCy, float size, float depth, Color fill, Color textCol, int number)
    {
        var v = NextCube();
        v.go.SetActive(true);
        v.go.transform.position = P(canvasCx, canvasCy, 0f);
        v.go.transform.localScale = new Vector3(size, size, depth);
        SetColor(v.r, fill);

        if (number > 0 && numberFont != null)
        {
            v.textGo.SetActive(true);
            string str = number.ToString();
            if (v.tm.text != str) v.tm.text = str;
            v.tm.color = textCol;
            float natural = v.tm.fontSize * v.tm.characterSize * 0.1f;
            float scale = (size * 0.55f) / Mathf.Max(0.001f, natural);
            v.textGo.transform.position = P(canvasCx, canvasCy, -(depth / 2f) - 0.04f);
            v.textGo.transform.localScale = Vector3.one * scale;
            v.textGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // face camera (-z)
        }
        else v.textGo.SetActive(false);
    }

    void DrawBall(float canvasX, float canvasY, float diameter, Color col)
    {
        var b = NextBall();
        b.go.SetActive(true);
        b.go.transform.position = P(canvasX, canvasY, -diameter * 0.5f);
        b.go.transform.localScale = Vector3.one * diameter;
        SetColor(b.r, col);
    }

    CubeVis NextCube()
    {
        if (cubeCursor < cubePool.Count) return cubePool[cubeCursor++];
        var go = new GameObject("cube");
        go.transform.SetParent(poolRoot, false);
        go.AddComponent<MeshFilter>().sharedMesh = cubeMesh;
        var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = litMat;

        var textGo = new GameObject("num");
        textGo.transform.SetParent(poolRoot, false);
        var tm = textGo.AddComponent<TextMesh>();
        tm.font = numberFont; tm.fontSize = 64; tm.characterSize = 1;
        tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
        var tmr = textGo.GetComponent<MeshRenderer>();
        if (numberFont != null) tmr.sharedMaterial = numberFont.material;

        var v = new CubeVis { go = go, r = r, textGo = textGo, tm = tm };
        cubePool.Add(v); cubeCursor++;
        return v;
    }

    BallVis NextBall()
    {
        if (ballCursor < ballPool.Count) return ballPool[ballCursor++];
        var go = new GameObject("ball");
        go.transform.SetParent(poolRoot, false);
        go.AddComponent<MeshFilter>().sharedMesh = sphereMesh;
        var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = litMat;
        var v = new BallVis { go = go, r = r };
        ballPool.Add(v); ballCursor++;
        return v;
    }

    // ── HUD (IMGUI, shared layout with 2D version) ──────────────────────────
    GUIStyle stLabel, stValue, stTitle, stPill, stModalTitle, stBig, stBody, stLb;
    void EnsureStyles()
    {
        if (stLabel != null) return;
        stLabel = new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = Hex(0x6F628F) } };
        stValue = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, normal = { textColor = Hex(0x2B1F4D) } };
        stTitle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = Hex(0xFFFFFF) } };
        stPill  = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = Hex(0xFF4E78) } };
        stModalTitle = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Hex(0x2B1F4D) } };
        stBig   = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Hex(0xFF4E78) } };
        stBody  = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = Hex(0x6F628F) } };
        stLb    = new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = Hex(0xEDE9F5) } };
    }

    void OnGUI()
    {
        if (s == null) return;
        EnsureStyles();

        float guiScale = Application.isMobilePlatform ? Mathf.Max(1f, Screen.dpi / 160f) : 1f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(guiScale, guiScale, 1f));
        GW = Screen.width / guiScale;
        GH = Screen.height / guiScale;

        var L = s.CurLevel;

        GUI.Label(new Rect(16, 12, 300, 28), "Block Bounce 3D", stTitle);
        GUI.Label(new Rect(190, 16, 120, 20), s.demoMode ? "DEMO ROUND" : "LIVE MATCH", stPill);
        GUI.Label(new Rect(300, 16, 260, 20), $"Level {(s.level + 1):00} · {L.name}", new GUIStyle(stLabel){ normal = { textColor = Hex(0xCFC8E0) } });

        DrawStat(16,  46, "SCORE", s.score.ToString(), 0xFF4E78);
        DrawStat(146, 46, "BALLS", s.BallsDisplay.ToString(), 0x6B5BFF);
        DrawStat(276, 46, "ROWS",  s.totalLines.ToString(), 0x11A877);
        DrawStat(406, 46, "LEVEL", (s.level + 1).ToString("00"), 0x2B1F4D);

        string goal = s.demoMode
            ? $"Reach {s.demoTarget} pts · {Mathf.Min(s.score, s.demoTarget)}/{s.demoTarget}"
            : $"Clear {L.lines} rows · {s.levelLines}/{L.lines}";
        GUI.Label(new Rect(16, 104, 520, 20), "STAGE GOAL: " + goal, new GUIStyle(stLabel){ normal = { textColor = Hex(0xCFC8E0) } });

        if (GUI.Button(new Rect(GW - 180, 14, 80, 28), s.phase == "paused" ? "Resume" : "Pause")) s.TogglePause();
        if (GUI.Button(new Rect(GW - 94, 14, 80, 28), "Quit")) { s.NewGame(true, 50); }

        DrawLeaderboard();
        DrawTouchControls();
        GUI.Label(new Rect(16, GH - 24, GW - 32, 20),
            "Keys: ←/→ · ↑ rotate · ↓ soft · Space hard · P pause · Click launch    —    Touch: buttons below + drag/tap to aim",
            new GUIStyle(stLabel){ normal = { textColor = Hex(0xCFC8E0) } });

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
        GUI.Label(new Rect(x + 10, y + 18, 110, 30), value, new GUIStyle(stValue) { normal = { textColor = Hex(color) } });
    }

    void DrawTouchControls()
    {
        if (s.phase != "play") return;
        float bh = 64, gap = 10, y = GH - bh - 44;
        var arrow = new GUIStyle(GUI.skin.button) { fontSize = 26, fontStyle = FontStyle.Bold };
        var word  = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold };
        if (GUI.Button(new Rect(16, y, bh, bh), "◀", arrow)) s.MoveLeft();
        if (GUI.Button(new Rect(16 + bh + gap, y, bh, bh), "▶", arrow)) s.MoveRight();
        float wbtn = 84;
        float rx = GW - (wbtn * 3 + gap * 2) - 16;
        if (GUI.Button(new Rect(rx, y, wbtn, bh), "Rotate", word)) s.TryRotate();
        if (GUI.RepeatButton(new Rect(rx + wbtn + gap, y, wbtn, bh), "Soft", word)) softDownFrame = Time.frameCount;
        if (GUI.Button(new Rect(rx + (wbtn + gap) * 2, y, wbtn, bh), "Drop", word)) s.HardDrop();
    }

    void DrawLeaderboard()
    {
        float x = GW - 220, y = 64, w = 206;
        GUI.Box(new Rect(x, y, w, 372), GUIContent.none);
        bool live = LeaderboardService.Configured && LeaderboardService.Loaded;
        var head = new GUIStyle(stLabel) { normal = { textColor = Hex(0xCFC8E0) } };
        GUI.Label(new Rect(x + 12, y + 8, w - 20, 20), live ? "LEADERBOARD  ● LIVE" : "LEADERBOARD  (local)", head);
        var lvlStyle = new GUIStyle(stLb) { fontSize = 10, normal = { textColor = Hex(0xA89CC1) } };

        if (live)
        {
            var top = LeaderboardService.Top;
            for (int i = 0; i < top.Count && i < 10; i++)
            {
                var e = top[i];
                bool me = e.name == playerName;
                float ry = y + 34 + i * 32;
                var style = new GUIStyle(stLb);
                if (me) style.normal.textColor = Hex(0xFF6B9D);
                if (i < 3) style.fontStyle = FontStyle.Bold;
                GUI.Label(new Rect(x + 12, ry, 22, 24), (i + 1).ToString(), style);
                GUI.Label(new Rect(x + 36, ry, 92, 24), e.name + (me ? " (YOU)" : ""), style);
                GUI.Label(new Rect(x + 130, ry, 28, 24), "L" + e.level, lvlStyle);
                GUI.Label(new Rect(x + w - 60, ry, 56, 24), e.score.ToString("N0"), style);
            }
        }
        else
        {
            var list = BBData.BuildLeaderboard(string.IsNullOrEmpty(playerName) ? "You" : playerName, Mathf.Max(best, s.score));
            for (int i = 0; i < list.Count; i++)
            {
                var row = list[i];
                float ry = y + 34 + i * 32;
                var style = new GUIStyle(stLb);
                if (row.me) style.normal.textColor = Hex(0xFF6B9D);
                if (i < 3) style.fontStyle = FontStyle.Bold;
                GUI.Label(new Rect(x + 12, ry, 22, 24), (i + 1).ToString(), style);
                GUI.Label(new Rect(x + 36, ry, 110, 24), row.name + (row.me ? " (YOU)" : ""), style);
                GUI.Label(new Rect(x + w - 60, ry, 56, 24), row.score.ToString("N0"), style);
            }
        }
    }

    Rect ModalRect() => new Rect(GW / 2f - 190, GH / 2f - 130, 380, 260);
    void ModalBg() { var c = GUI.color; GUI.color = new Color(0.17f, 0.12f, 0.30f, 0.55f); GUI.DrawTexture(new Rect(0, 0, GW, GH), Texture2D.whiteTexture); GUI.color = c; }

    void Modal(string title, string big, string body, string btn, System.Action onBtn)
    {
        ModalBg(); var r = ModalRect(); GUI.Box(r, GUIContent.none);
        GUI.Label(new Rect(r.x, r.y + 24, r.width, 32), title, stModalTitle);
        if (!string.IsNullOrEmpty(big)) GUI.Label(new Rect(r.x, r.y + 64, r.width, 50), big, stBig);
        GUI.Label(new Rect(r.x + 24, r.y + 120, r.width - 48, 60), body, stBody);
        if (GUI.Button(new Rect(r.x + r.width / 2f - 90, r.y + r.height - 56, 180, 40), btn)) onBtn();
    }

    void DrawStartModal()
    {
        ModalBg(); var r = ModalRect(); GUI.Box(r, GUIContent.none);
        GUI.Label(new Rect(r.x, r.y + 20, r.width, 32), "Block Bounce 3D", stModalTitle);
        GUI.Label(new Rect(r.x + 24, r.y + 60, r.width - 48, 44),
            s.demoMode ? $"Drop pieces, mint balls, shatter the stack. Reach {s.demoTarget} points to unlock the live match."
                       : "10 levels, escalating tier. Every shatter counts.", stBody);
        GUI.Label(new Rect(r.x + 24, r.y + 118, 120, 20), "Player name:", stLabel);
        playerName = GUI.TextField(new Rect(r.x + 24, r.y + 138, r.width - 48, 28), playerName, 16);
        if (GUI.Button(new Rect(r.x + r.width / 2f - 90, r.y + r.height - 52, 180, 40), s.demoMode ? "Start Demo Round →" : "Start Live Match →"))
        { PlayerPrefs.SetString("bb_name", playerName); s.StartGame(); }
    }

    void DrawDemoDone()
    {
        ModalBg(); var r = ModalRect(); GUI.Box(r, GUIContent.none);
        GUI.Label(new Rect(r.x, r.y + 18, r.width, 28), "Nice — demo cleared!", stModalTitle);
        GUI.Label(new Rect(r.x, r.y + 52, r.width, 48), s.score.ToString(), stBig);
        GUI.Label(new Rect(r.x + 24, r.y + 108, r.width - 48, 40), $"You hit {s.demoTarget} points. Ready to take it live across 10 levels?", stBody);
        if (GUI.Button(new Rect(r.x + 30, r.y + r.height - 52, 150, 40), "Play Again")) { s.NewGame(true, 50); s.StartGame(); }
        if (GUI.Button(new Rect(r.x + r.width - 180, r.y + r.height - 52, 150, 40), "Start Live →")) { s.NewGame(false, 50); s.StartGame(); }
    }

    void DrawLevelUp()
    {
        ModalBg(); var r = ModalRect();
        int nextIdx = s.PendingLevelUpIdx;
        var nl = nextIdx >= 0 ? BBData.Levels[nextIdx] : s.CurLevel;
        GUI.Box(r, GUIContent.none);
        GUI.Label(new Rect(r.x, r.y + 18, r.width, 28), $"Level {(s.level + 1):00} cleared", stModalTitle);
        GUI.Label(new Rect(r.x, r.y + 52, r.width, 48), "+" + ((s.level + 1) * 500), stBig);
        GUI.Label(new Rect(r.x + 24, r.y + 108, r.width - 48, 44), $"Up next: {nl.name}. Cells shrink to {nl.cell}px, HP {nl.hpLo}–{nl.hpHi}, {nl.bpr} balls per row.", stBody);
        if (GUI.Button(new Rect(r.x + r.width / 2f - 90, r.y + r.height - 52, 180, 40), $"Begin Level {nextIdx + 1} →")) s.BeginPendingLevel();
    }

    void DrawWon()
    {
        ModalBg(); var r = ModalRect(); GUI.Box(r, GUIContent.none);
        GUI.Label(new Rect(r.x, r.y + 18, r.width, 28), "Singularity cleared", stModalTitle);
        GUI.Label(new Rect(r.x, r.y + 52, r.width, 48), s.score.ToString(), stBig);
        GUI.Label(new Rect(r.x + 24, r.y + 108, r.width - 48, 44), $"You cleared all 10 levels and {s.totalLines} total rows.", stBody);
        if (GUI.Button(new Rect(r.x + 30, r.y + r.height - 52, 150, 40), "Quit")) s.NewGame(true, 50);
        if (GUI.Button(new Rect(r.x + r.width - 180, r.y + r.height - 52, 150, 40), "Play Again")) { s.NewGame(false, 50); s.StartGame(); }
    }

    void DrawOver()
    {
        ModalBg(); var r = ModalRect(); GUI.Box(r, GUIContent.none);
        GUI.Label(new Rect(r.x, r.y + 18, r.width, 28), "Stack overflow", stModalTitle);
        GUI.Label(new Rect(r.x, r.y + 52, r.width, 48), s.score.ToString(), stBig);
        GUI.Label(new Rect(r.x + 24, r.y + 108, r.width - 48, 44), $"The blocks reached the ceiling. You cleared {s.totalLines} row{(s.totalLines == 1 ? "" : "s")}.", stBody);
        if (GUI.Button(new Rect(r.x + 30, r.y + r.height - 52, 150, 40), "Quit")) s.NewGame(true, 50);
        if (GUI.Button(new Rect(r.x + r.width - 180, r.y + r.height - 52, 150, 40), s.demoMode ? "Play Again" : "From Level 1")) { s.NewGame(s.demoMode, 50); s.StartGame(); }
    }

    void DrawDeployPicker()
    {
        ModalBg();
        var r = new Rect(GW / 2f - 190, GH / 2f - 110, 380, 220);
        GUI.Box(r, GUIContent.none);
        GUI.Label(new Rect(r.x, r.y + 16, r.width, 28), "Deploy your balls", stModalTitle);
        GUI.Label(new Rect(r.x + 24, r.y + 50, r.width - 48, 24), $"{s.queuedBalls} balls ready · pick how to launch", stBody);
        if (GUI.Button(new Rect(r.x + 28, r.y + 96, 150, 90), "Aim & Shoot\n\nMouse + click")) s.ChooseAim();
        if (GUI.Button(new Rect(r.x + r.width - 178, r.y + 96, 150, 90), "Random Spray\n\nAuto-launch")) s.ChooseRandom();
    }

    void DrawAimHint()
    {
        GUI.Label(new Rect(GW / 2f - 160, 80, 320, 24), "Move mouse to aim · Click to launch", stModalTitle);
        if (GUI.Button(new Rect(GW / 2f - 90, GH - 120, 180, 30), "↻ Switch to Random Spray")) s.ChooseRandom();
    }

    static Color Hex(int rgb, float a = 1f) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);
    static Color ToColor(RGB c, float a = 1f) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, a);
}
