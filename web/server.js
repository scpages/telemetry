'use strict';

// Entry point: tiny router over node:http. Zero dependencies.

const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const config = require('./config');
const store = require('./lib/store');
const session = require('./lib/session');
const sentry = require('./lib/sentry');
const { sendJson, redirect } = require('./lib/http');
const auth = require('./handlers/auth');
const pages = require('./handlers/pages');
const api = require('./handlers/api');

// [method, pathPattern, handler]. ":name" captures a path segment.
// Site icon, read once at boot (falls back to empty if the file is missing).
let FAVICON = Buffer.alloc(0);
try { FAVICON = fs.readFileSync(path.join(__dirname, 'public', 'favicon.ico')); } catch { /* no icon */ }

const routes = [
  ['GET', '/', pages.landing],
  ['GET', '/health', (_req, res) => sendJson(res, 200, { ok: true })],
  ['GET', '/favicon.ico', (_req, res) => {
    res.writeHead(200, { 'content-type': 'image/x-icon', 'cache-control': 'public, max-age=86400' });
    res.end(FAVICON);
  }],

  ['GET', '/login', auth.login],
  ['GET', '/auth/discord/callback', auth.callback],
  ['GET', '/logout', auth.logout],
  ['GET', '/dev-login', auth.devLoginForm],
  ['POST', '/dev-login', auth.devLoginSubmit],

  // sources (formerly "account")
  ['GET', '/dashboard', (_req, res) => redirect(res, '/sources')],
  ['GET', '/account', (_req, res) => redirect(res, '/sources')],
  ['GET', '/sources', pages.sourcesPage],
  ['POST', '/sources', pages.createSource],
  ['GET', '/sources/:id/data', pages.sourceDataPage],
  ['GET', '/sources/:id/edit', pages.sourceEditPage],
  ['POST', '/sources/:id/limit', pages.setSourceLimit],
  ['POST', '/sources/:id/rotate', pages.rotateSource],
  ['POST', '/sources/:id/delete', pages.deleteSource],

  ['GET', '/groups', pages.groupsPage],
  ['POST', '/groups', pages.createGroup],
  ['GET', '/groups/:id', pages.groupPage],
  ['POST', '/groups/:id/remove', pages.removeMember],
  ['POST', '/groups/:id/set-owner', pages.setOwner],
  ['POST', '/groups/:id/rotate-key', pages.rotateGroupKey],
  ['POST', '/groups/:id/delete', pages.deleteGroup],

  ['GET', '/join/:code', pages.joinPage],
  ['POST', '/join/:code', pages.joinSubmit],

  ['GET', '/about', pages.about],
  ['GET', '/api', pages.apiDocs],

  // JSON API — versioned (/api/v1) is canonical; /api/* and /telemetry kept as aliases
  ['GET', '/api/v1/ping', api.ping],
  ['POST', '/api/v1/telemetry', api.ingest],
  ['GET', '/api/v1/telemetry', api.sourceRead],
  ['GET', '/api/v1/groups/:id/latest', api.groupLatest],
  ['GET', '/api/v1/groups/:id/history', api.groupHistory],

  ['GET', '/api/ping', api.ping],
  ['POST', '/api/telemetry', api.ingest],
  ['POST', '/telemetry', api.ingest],
  ['GET', '/api/telemetry', api.sourceRead],
  ['GET', '/api/groups/:id/latest', api.groupLatest],
  ['GET', '/api/groups/:id/history', api.groupHistory],
];

const compiled = routes.map(([method, pattern, handler]) => {
  const names = [];
  const rx = new RegExp('^' + pattern.replace(/:[^/]+/g, (m) => { names.push(m.slice(1)); return '([^/]+)'; }) + '$');
  return { method, rx, names, handler };
});

function match(method, pathname) {
  for (const r of compiled) {
    if (r.method !== method) continue;
    const m = r.rx.exec(pathname);
    if (!m) continue;
    const params = {};
    r.names.forEach((n, i) => { params[n] = decodeURIComponent(m[i + 1]); });
    return { handler: r.handler, params };
  }
  return null;
}

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, config.baseUrl);
  const route = match(req.method, url.pathname);
  if (!route) return sendJson(res, 404, { error: 'not found' });

  const ctx = {
    store,
    params: route.params,
    query: Object.fromEntries(url.searchParams),
    url,
    user: null,
  };
  const uid = session.userIdFrom(req);
  if (uid) ctx.user = store.getUser(uid);

  try {
    await route.handler(req, res, ctx);
  } catch (e) {
    console.error(`${req.method} ${url.pathname}:`, e.message);
    // Report the failure (no cookies/auth headers — avoid leaking secrets/PII).
    sentry.captureException(e, {
      request: { method: req.method, url: url.href, headers: { 'user-agent': req.headers['user-agent'] } },
      tags: { route: url.pathname },
    });
    if (!res.writableEnded) sendJson(res, 500, { error: 'internal error' });
  }
});

// Catch failures outside the request handler so they still reach Sentry.
process.on('unhandledRejection', (reason) => {
  console.error('unhandledRejection:', reason);
  sentry.captureException(reason, { tags: { kind: 'unhandledRejection' } });
});
process.on('uncaughtException', (err) => {
  console.error('uncaughtException:', err);
  sentry.captureException(err, { tags: { kind: 'uncaughtException' } });
  // State may be unreliable after this — report, then exit (default Node behaviour).
  setTimeout(() => process.exit(1), 2000).unref();
});

server.listen(config.port, config.host, () => {
  console.log(`${config.app.name} web on http://${config.host}:${config.port}  (base ${config.baseUrl})`);
  console.log(`  discord login: ${config.discordConfigured ? 'configured' : 'NOT configured'}${config.allowDevLogin ? ', dev-login ON' : ''}`);
  console.log(`  sentry: ${sentry.enabled ? `on (${config.sentry.environment})` : 'off'}`);
});
