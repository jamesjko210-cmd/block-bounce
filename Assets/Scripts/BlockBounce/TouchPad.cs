// TouchPad.cs — on-screen touch controls shared by the 2D and 3D games.
//
// • Buttons are sized as a fraction of the *physical* screen (drawn under an
//   identity GUI matrix), so they're large and consistent on any phone/tablet.
// • Layout is user-editable: tap "Move buttons", drag each button anywhere,
//   tap Done. Positions are stored per-button (normalized 0..1) in PlayerPrefs,
//   so they persist and are shared between the 2D and 3D versions.
//
// Usage each frame (inside OnGUI, during the "play" phase):
//   pad.Draw();
//   if (pad.Left) s.MoveLeft();  ... etc.  if (pad.SoftHeld) softDownFrame = Time.frameCount;
// And freeze the sim while editing: if (!pad.EditMode) s.Step(dt);

using System.Collections.Generic;
using UnityEngine;

public class TouchPad
{
    // results for the current OnGUI pass
    public bool Left, Right, Rotate, Drop, SoftHeld;
    public bool EditMode { get; private set; }

    class B { public string id, label; public bool wide; public Vector2 def, pos; }

    readonly List<B> buttons = new List<B>
    {
        new B { id = "left",   label = "◀",      def = new Vector2(0.10f, 0.90f) },
        new B { id = "right",  label = "▶",      def = new Vector2(0.26f, 0.90f) },
        new B { id = "rotate", label = "Rotate", wide = true, def = new Vector2(0.87f, 0.66f) },
        new B { id = "soft",   label = "Soft",   wide = true, def = new Vector2(0.87f, 0.78f) },
        new B { id = "drop",   label = "Drop",   wide = true, def = new Vector2(0.87f, 0.90f) },
    };

    int dragging = -1;
    bool loaded;

    void Load()
    {
        foreach (var b in buttons)
            b.pos = new Vector2(
                PlayerPrefs.GetFloat("bb_btn_" + b.id + "_x", b.def.x),
                PlayerPrefs.GetFloat("bb_btn_" + b.id + "_y", b.def.y));
        loaded = true;
    }

    void Save(B b)
    {
        PlayerPrefs.SetFloat("bb_btn_" + b.id + "_x", b.pos.x);
        PlayerPrefs.SetFloat("bb_btn_" + b.id + "_y", b.pos.y);
    }

    public void ResetLayout()
    {
        foreach (var b in buttons) { b.pos = b.def; Save(b); }
    }

    // Call when the pad isn't being drawn (e.g. paused/menu) so edit mode can't
    // get stuck on, which would otherwise freeze the sim and hide the Done button.
    public void CancelEdit() { EditMode = false; dragging = -1; }

    public void Draw()
    {
        if (!loaded) Load();
        Left = Right = Rotate = Drop = false;
        SoftHeld = false;

        // Draw in real screen pixels so button size is a consistent fraction of
        // the device's screen, regardless of the HUD's DPI matrix.
        var prevMatrix = GUI.matrix;
        var prevColor = GUI.color;
        GUI.matrix = Matrix4x4.identity;

        float sw = Screen.width, sh = Screen.height;
        float unit = Mathf.Min(sw, sh);
        float h = Mathf.Clamp(unit * 0.13f, 72f, 260f);     // button height ≈ 13% of short edge
        float wNarrow = h, wWide = h * 1.4f;
        var arrow = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(h * 0.44f), fontStyle = FontStyle.Bold };
        var word  = new GUIStyle(GUI.skin.button) { fontSize = Mathf.Max(12, Mathf.RoundToInt(h * 0.26f)), fontStyle = FontStyle.Bold };

        var e = Event.current;

        for (int i = 0; i < buttons.Count; i++)
        {
            var b = buttons[i];
            float w = b.wide ? wWide : wNarrow;
            float halfWx = (w / sw) / 2f, halfHy = (h / sh) / 2f;
            // keep fully on-screen
            b.pos.x = Mathf.Clamp(b.pos.x, halfWx, 1f - halfWx);
            b.pos.y = Mathf.Clamp(b.pos.y, halfHy, 1f - halfHy);
            Rect r = new Rect(b.pos.x * sw - w / 2f, b.pos.y * sh - h / 2f, w, h);

            if (EditMode)
            {
                var box = new GUIStyle(GUI.skin.box) { fontSize = word.fontSize, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
                GUI.color = dragging == i ? new Color(1f, 0.31f, 0.47f, 0.95f) : new Color(0.42f, 0.36f, 1f, 0.9f);
                GUI.Box(r, b.label + "  ⤢", box);
                GUI.color = prevColor;

                if (e.type == EventType.MouseDown && r.Contains(e.mousePosition)) { dragging = i; e.Use(); }
                else if (dragging == i)
                {
                    if (e.type == EventType.MouseDrag) { b.pos = new Vector2(e.mousePosition.x / sw, e.mousePosition.y / sh); e.Use(); }
                    else if (e.type == EventType.MouseUp) { Save(b); dragging = -1; e.Use(); }
                }
            }
            else if (b.id == "soft")
            {
                if (GUI.RepeatButton(r, "Soft", word)) SoftHeld = true;
            }
            else if (GUI.Button(r, b.label, b.wide ? word : arrow))
            {
                if (b.id == "left") Left = true;
                else if (b.id == "right") Right = true;
                else if (b.id == "rotate") Rotate = true;
                else if (b.id == "drop") Drop = true;
            }
        }

        // edit toggle / reset (bottom-center)
        float tw = Mathf.Clamp(unit * 0.26f, 130f, 360f);
        float th = Mathf.Clamp(unit * 0.07f, 40f, 110f);
        var tstyle = new GUIStyle(GUI.skin.button) { fontSize = Mathf.Max(12, Mathf.RoundToInt(th * 0.34f)), fontStyle = FontStyle.Bold };
        if (!EditMode)
        {
            if (GUI.Button(new Rect(sw / 2f - tw / 2f, sh - th - 8f, tw, th), "⚙ Move buttons", tstyle)) EditMode = true;
        }
        else
        {
            if (GUI.Button(new Rect(sw / 2f - tw - 6f, sh - th - 8f, tw, th), "✓ Done", tstyle)) { EditMode = false; dragging = -1; }
            if (GUI.Button(new Rect(sw / 2f + 6f, sh - th - 8f, tw, th), "↺ Reset", tstyle)) ResetLayout();
        }

        GUI.matrix = prevMatrix;
        GUI.color = prevColor;
    }
}
