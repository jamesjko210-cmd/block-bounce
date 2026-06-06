/**
 * Block Bounce — live leaderboard backend (Google Apps Script).
 *
 * Paste this into your Google Sheet:  Extensions → Apps Script → replace Code.gs.
 * Then set SECRET below, and Deploy → New deployment → Web app
 *   • Execute as: Me
 *   • Who has access: Anyone
 * Copy the resulting /exec URL into Unity's LeaderboardService.EndpointUrl,
 * and put the same SECRET into LeaderboardService.SecretToken.
 */

const SHEET_NAME = 'Scores';
const SECRET     = 'CHANGE_ME_TOKEN';   // must match Unity LeaderboardService.SecretToken
const TOP_N      = 10;

function getSheet_() {
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  let sh = ss.getSheetByName(SHEET_NAME);
  if (!sh) {
    sh = ss.insertSheet(SHEET_NAME);
    sh.appendRow(['timestamp', 'name', 'score', 'level']);
  }
  return sh;
}

// Read: return the top scores.
function doGet(e) {
  return top_();
}

// Write: validate token, append one score row, return the updated top.
function doPost(e) {
  const p = (e && e.parameter) || {};
  if (p.token !== SECRET) return json_({ ok: false, error: 'bad token' });

  const name  = sanitize_(p.name);
  const score = Math.max(0, Math.min(100000000, parseInt(p.score, 10) || 0));
  const level = Math.max(1, Math.min(10, parseInt(p.level, 10) || 1));

  if (name.length >= 1) {
    getSheet_().appendRow([new Date(), name, score, level]);
  }
  return top_();
}

// Best score per name, sorted high→low, capped at TOP_N.
function top_() {
  const sh = getSheet_();
  const rows = sh.getDataRange().getValues(); // row 0 is the header
  const best = {};
  for (let i = 1; i < rows.length; i++) {
    const name = rows[i][1];
    if (!name) continue;
    const score = Number(rows[i][2]) || 0;
    const level = Number(rows[i][3]) || 1;
    const key = String(name);
    if (!best[key] || score > best[key].score) {
      best[key] = { name: key, score: score, level: level };
    }
  }
  const list = Object.keys(best).map(function (k) { return best[k]; })
    .sort(function (a, b) { return b.score - a.score; })
    .slice(0, TOP_N);
  return json_({ ok: true, entries: list });
}

function sanitize_(s) {
  return String(s || '').trim().slice(0, 16).replace(/[<>]/g, '');
}

function json_(obj) {
  return ContentService.createTextOutput(JSON.stringify(obj))
    .setMimeType(ContentService.MimeType.JSON);
}
