'use strict';

// Server-side HTML: escaping + a shared RSI-inspired layout. No template engine.

const config = require('../config');

function esc(s) {
  return String(s ?? '').replace(/[&<>"']/g, (c) => (
    { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]
  ));
}

const STYLE = `
  :root {
    color-scheme: dark;
    --bg: #05080c;
    --bg2: #070d14;
    --panel: #0a131d;
    --panel2: #0c1a28;
    --line: rgba(0,183,224,0.22);
    --accent: #15c6ec;
    --accent-dim: #0e5e73;
    --text: #b9ccd8;
    --head: #eaf7fc;
    --muted: #5f7686;
    --danger: #ff5a52;
    --display: "Rajdhani", "Bahnschrift", "DIN Alternate", system-ui, sans-serif;
    --logo: "Orbitron", var(--display);
  }
  * { box-sizing: border-box; }
  html, body { height: 100%; }
  body {
    margin: 0; background: var(--bg); color: var(--text);
    min-width: 1000px;            /* keep the desktop layout; phones zoom/scroll instead of wrapping */
    font: 15px/1.6 system-ui, "Segoe UI", sans-serif;
    background-image:
      radial-gradient(1200px 600px at 75% -10%, rgba(21,198,236,0.08), transparent 60%),
      linear-gradient(180deg, #05080c 0%, #060c13 100%);
    background-attachment: fixed;
  }
  /* faint HUD grid */
  body::before {
    content: ""; position: fixed; inset: 0; pointer-events: none; z-index: 0;
    background-image:
      linear-gradient(rgba(21,198,236,0.035) 1px, transparent 1px),
      linear-gradient(90deg, rgba(21,198,236,0.035) 1px, transparent 1px);
    background-size: 44px 44px;
    mask-image: linear-gradient(180deg, rgba(0,0,0,0.6), transparent 70%);
  }
  .app, .center { position: relative; z-index: 1; }

  a { color: var(--accent); text-decoration: none; }
  a:hover { color: #7fe6ff; text-decoration: none; }

  h1, h2 {
    font-family: var(--display); text-transform: uppercase; letter-spacing: 2px;
    color: var(--head); font-weight: 600; margin: 0 0 12px;
  }
  h1 { font-size: 24px; padding-left: 14px; position: relative; }
  h1::before { content: ""; position: absolute; left: 0; top: 2px; bottom: 2px; width: 4px; background: var(--accent); box-shadow: 0 0 10px var(--accent); }
  h2 { font-size: 15px; margin-top: 34px; color: var(--accent); letter-spacing: 3px; }

  /* layout */
  .app { display: flex; min-height: 100vh; }
  .side {
    width: 244px; flex: 0 0 244px; background: linear-gradient(180deg, #081019, #060b11);
    border-right: 1px solid var(--line); padding: 22px 0; display: flex; flex-direction: column;
  }
  .side .brand {
    font-family: var(--logo); font-weight: 700; letter-spacing: 2px; font-size: 19px;
    color: var(--head); padding: 0 20px;
  }
  .side .brand b { color: var(--accent); }
  .side .tag { padding: 6px 20px 20px; color: var(--muted); font-size: 11px; text-transform: uppercase; letter-spacing: 2px; border-bottom: 1px solid var(--line); margin-bottom: 10px; }
  .side nav { display: flex; flex-direction: column; }
  .side nav a {
    font-family: var(--display); text-transform: uppercase; letter-spacing: 2px; font-size: 14px;
    padding: 12px 20px; color: #9fb4c2; border-left: 3px solid transparent;
  }
  .side nav a:hover { background: rgba(21,198,236,0.06); color: var(--head); }
  .side nav a.active { background: linear-gradient(90deg, rgba(21,198,236,0.14), transparent); border-left-color: var(--accent); color: var(--head); text-shadow: 0 0 10px rgba(21,198,236,0.5); }
  .side .foot { margin-top: auto; padding: 14px 20px; color: var(--muted); font-size: 11px; letter-spacing: 1px; border-top: 1px solid var(--line); }

  .content { flex: 1; min-width: 0; }
  main { max-width: 900px; margin: 0 auto; padding: 28px 28px 56px; }

  /* panels — chamfered HUD corners */
  .card {
    background: linear-gradient(180deg, var(--panel), var(--bg2));
    border: 1px solid var(--line); border-top: 2px solid var(--accent);
    padding: 16px 18px; margin: 14px 0; position: relative;
    clip-path: polygon(0 0, 100% 0, 100% calc(100% - 14px), calc(100% - 14px) 100%, 0 100%);
    box-shadow: 0 0 0 1px rgba(0,0,0,0.3), 0 8px 24px rgba(0,0,0,0.35);
  }
  .card::after {
    content: ""; position: absolute; right: 8px; top: 8px; width: 6px; height: 6px;
    background: var(--accent); box-shadow: 0 0 8px var(--accent);
  }

  .table-wrap { overflow-x: auto; }
  table { width: 100%; border-collapse: collapse; }
  .nowrap { white-space: nowrap; }
  th, td { text-align: left; padding: 9px 10px; border-bottom: 1px solid rgba(255,255,255,0.05); vertical-align: top; }
  th { color: var(--muted); font-family: var(--display); font-weight: 600; font-size: 12px; text-transform: uppercase; letter-spacing: 2px; }

  code, pre { font-family: ui-monospace, "Cascadia Mono", Consolas, monospace; }
  code { background: #04090e; color: #8fe9ff; padding: 2px 6px; border: 1px solid var(--line); border-radius: 2px; word-break: break-all; }
  code.tok { word-break: normal; white-space: nowrap; }
  td .actions { display: flex; flex-wrap: wrap; gap: 6px; }
  pre { background: #04090e; border: 1px solid var(--line); border-left: 2px solid var(--accent); padding: 12px; color: #cfe8f2; white-space: pre-wrap; overflow-wrap: anywhere; }

  input[type=text], input[type=number], select {
    background: #04090e; color: var(--head); border: 1px solid var(--line);
    padding: 8px 10px; font: inherit; border-radius: 0;
    margin-right: 8px; vertical-align: middle;
  }
  input:focus, select:focus { outline: none; border-color: var(--accent); box-shadow: 0 0 0 1px var(--accent), 0 0 12px rgba(21,198,236,0.35); }

  button, .btn {
    font-family: var(--display); text-transform: uppercase; letter-spacing: 2px; font-weight: 600; font-size: 13px;
    background: var(--accent); color: #04171d; border: 0; padding: 9px 16px; cursor: pointer;
    clip-path: polygon(8px 0, 100% 0, 100% calc(100% - 8px), calc(100% - 8px) 100%, 0 100%, 0 8px);
    transition: filter .15s, background .15s;
  }
  button:hover, .btn:hover { filter: brightness(1.15); }
  button.secondary, .btn.secondary { background: #1b2a38; color: #bfe6f2; }
  button.danger, .btn.danger { background: #3a1416; color: var(--danger); border: 1px solid rgba(255,90,82,0.4); }
  button.danger:hover { background: #511a1d; }
  .discord { background: var(--accent); color: #04171d; display: inline-block; }

  form.inline { display: inline; }
  .muted { color: var(--muted); }
  .pill {
    font-family: var(--display); text-transform: uppercase; letter-spacing: 2px; font-size: 11px;
    background: rgba(21,198,236,0.12); color: var(--accent); border: 1px solid var(--line); padding: 2px 10px;
  }
  details summary { cursor: pointer; color: var(--accent); }
  .method { font-family: var(--display); font-weight: 700; letter-spacing: 2px; font-size: 12px; padding: 2px 10px; border: 1px solid var(--line); }
  .method.get { color: var(--accent); background: rgba(21,198,236,0.1); }
  .method.post { color: #5fe39b; background: rgba(95,227,155,0.1); border-color: rgba(95,227,155,0.3); }
  .ep { margin-bottom: 4px; display: flex; align-items: center; gap: 10px; }
  .ep code { font-size: 14px; }

  .center { max-width: 560px; margin: 14vh auto; padding: 0 20px; text-align: center; }
  .center .brand { font-family: var(--logo); font-weight: 700; letter-spacing: 4px; font-size: 34px; color: var(--head); }
  .center .brand b { color: var(--accent); }
  .center .tag { color: var(--muted); text-transform: uppercase; letter-spacing: 3px; font-size: 12px; margin: 8px 0 24px; }
  .center .card { text-align: left; }
`;

const FONTS = '<link rel="preconnect" href="https://fonts.googleapis.com">'
  + '<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>'
  + '<link href="https://fonts.googleapis.com/css2?family=Orbitron:wght@500;700&family=Rajdhani:wght@500;600;700&display=swap" rel="stylesheet">';

const TABS = [
  { key: 'sources', href: '/sources', label: 'Sources' },
  { key: 'groups', href: '/groups', label: 'Groups' },
  { key: 'api', href: '/api', label: 'API docs' },
  { key: 'about', href: '/about', label: 'About' },
  { key: 'logout', href: '/logout', label: 'Log out' },
];

function brandMarkup() {
  return `SC<b>TELEMETRY</b>`;
}

function sidebar(active) {
  const links = TABS.map((t) =>
    `<a href="${t.href}"${t.key === active ? ' class="active"' : ''}>${t.label}</a>`
  ).join('');
  return `<aside class="side">
    <div class="brand">${brandMarkup()}</div>
    <div class="tag">${esc(config.app.tagline)}</div>
    <nav>${links}</nav>
    <div class="foot">BUILD v${esc(config.app.version)}</div>
  </aside>`;
}

// Logged-in layout with the left HUD panel.
function layout({ title, user, active, body }) {
  if (!user) return plain({ title, body });
  return page(title, `<div class="app">${sidebar(active)}<div class="content"><main>${body}</main></div></div>`);
}

// Centered layout for logged-out pages.
function plain({ title, body }) {
  return page(title, `<div class="center">
    <div class="brand">${brandMarkup()}</div>
    <div class="tag">${esc(config.app.tagline)}</div>
    ${body}</div>`);
}

function page(title, inner) {
  return `<!doctype html>
<html lang="en"><head>
<meta charset="utf-8">
<meta name="viewport" content="width=1000">
<title>${esc(title)} · ${esc(config.app.name)}</title>
<link rel="icon" href="/favicon.ico">
${FONTS}
<style>${STYLE}</style>
</head><body>${inner}</body></html>`;
}

module.exports = { esc, layout, plain };
