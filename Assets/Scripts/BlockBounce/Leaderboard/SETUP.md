# Live leaderboard setup (Google Sheets)

A working game ships **without** this — it falls back to a local demo board until
you complete these steps. No network calls happen until the URL below is filled in.

## 1. Make the Sheet + script
1. Create a new Google Sheet (any name).
2. **Extensions → Apps Script**.
3. Delete the default code, paste in everything from `AppsScript.gs`.
4. Change `const SECRET = 'CHANGE_ME_TOKEN';` to your own secret string.
5. Save.

## 2. Deploy as a Web App
1. **Deploy → New deployment**.
2. Gear icon → **Web app**.
3. **Execute as:** Me · **Who has access:** Anyone.
4. **Deploy**, authorize when prompted, and **copy the Web app URL** (ends in `/exec`).

## 3. Connect Unity
In `LeaderboardService.cs`, set:
```csharp
public const string EndpointUrl = "https://script.google.com/macros/s/XXXX/exec";
public const string SecretToken = "your-secret-string";   // same as SECRET above
```
Save → Unity recompiles → press Play. Scores submit on game-over and the live
top 10 appears in the leaderboard panel.

## Data stored
Per row: `timestamp, name, score, level`. Only a local nickname + score + level —
no personal data (matches the project's safety criterion).

## Notes
- **Whenever you change the script**, re-deploy: **Deploy → Manage deployments →
  edit (pencil) → Version: New version → Deploy** (or the URL serves old code).
- Works from the Unity Editor and native (iOS/desktop) builds.
- **WebGL/browser builds**: Apps Script has CORS quirks from a browser. Solvable
  later (e.g. a tiny proxy or `mode:no-cors` submit); not needed for Editor/native.
