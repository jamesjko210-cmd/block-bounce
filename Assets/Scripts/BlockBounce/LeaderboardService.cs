// LeaderboardService.cs — live leaderboard backed by a Google Sheet.
//
// Flow:  Unity  --HTTP-->  Google Apps Script Web App  -->  Google Sheet
//   • GET  the web-app URL            -> returns top entries as JSON
//   • POST name/score/level/token     -> appends a row, returns updated top
//
// Setup: see Leaderboard/SETUP.md. Paste your deployed Web App URL + token below.
// Until those are filled in, the game falls back to the local/demo leaderboard
// and makes no network calls (so it still runs fine offline / before setup).

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public static class LeaderboardService
{
    // ════════════════════ PASTE YOUR VALUES HERE ════════════════════
    // 1) Deploy the Apps Script (Leaderboard/AppsScript.gs) as a Web App and
    //    paste its /exec URL here.
    public const string EndpointUrl = "PASTE_YOUR_APPS_SCRIPT_WEB_APP_URL_HERE";
    // 2) Must match the SECRET in the Apps Script.
    public const string SecretToken = "CHANGE_ME_TOKEN";
    // ═════════════════════════════════════════════════════════════════

    [Serializable] public struct Entry { public string name; public int score; public int level; }
    [Serializable] class Resp { public bool ok; public Entry[] entries; }

    public static readonly List<Entry> Top = new List<Entry>();
    public static bool Loaded { get; private set; }
    public static bool Configured => EndpointUrl.StartsWith("http");

    static Runner runner;
    static Runner Run
    {
        get
        {
            if (runner == null)
            {
                var go = new GameObject("BBLeaderboardRunner");
                UnityEngine.Object.DontDestroyOnLoad(go);
                runner = go.AddComponent<Runner>();
            }
            return runner;
        }
    }

    public static void Fetch(Action onDone = null)
    {
        if (!Configured) { onDone?.Invoke(); return; }
        Run.StartCoroutine(FetchCo(onDone));
    }

    public static void Submit(string name, int score, int level, Action onDone = null)
    {
        if (!Configured) { onDone?.Invoke(); return; }
        Run.StartCoroutine(SubmitCo(name, score, level, onDone));
    }

    static IEnumerator FetchCo(Action onDone)
    {
        using (var req = UnityWebRequest.Get(EndpointUrl))
        {
            req.timeout = 10;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success) Parse(req.downloadHandler.text);
            else Debug.LogWarning("[BB] leaderboard fetch failed: " + req.error);
        }
        onDone?.Invoke();
    }

    static IEnumerator SubmitCo(string name, int score, int level, Action onDone)
    {
        var form = new WWWForm();
        form.AddField("token", SecretToken);
        form.AddField("name", string.IsNullOrEmpty(name) ? "You" : name);
        form.AddField("score", score);
        form.AddField("level", level);
        using (var req = UnityWebRequest.Post(EndpointUrl, form))
        {
            req.timeout = 10;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success) Parse(req.downloadHandler.text);
            else Debug.LogWarning("[BB] leaderboard submit failed: " + req.error);
        }
        onDone?.Invoke();
    }

    static void Parse(string txt)
    {
        try
        {
            var r = JsonUtility.FromJson<Resp>(txt);
            if (r != null && r.entries != null)
            {
                Top.Clear();
                Top.AddRange(r.entries);
                Loaded = true;
            }
        }
        catch (Exception e) { Debug.LogWarning("[BB] leaderboard parse error: " + e.Message); }
    }

    class Runner : MonoBehaviour { }
}
