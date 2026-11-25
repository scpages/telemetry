'use strict';

// Minimal Sentry error reporting over raw https — no SDK, no dependency.
// Active only when SENTRY_DSN is set in config/.env; never configured via the UI.

const https = require('node:https');
const crypto = require('node:crypto');
const os = require('node:os');
const config = require('../config');

// DSN: https://<publicKey>@<host>[:port]/<projectId>
function parseDsn(dsn) {
  try {
    const u = new URL(dsn);
    const projectId = u.pathname.replace(/^\/+/, '');
    if (!u.username || !projectId) return null;
    return {
      publicKey: u.username,
      hostname: u.hostname,
      port: u.port || 443,
      projectId,
      path: `/api/${projectId}/envelope/`,
    };
  } catch {
    return null;
  }
}

const dsn = config.sentry.dsn ? parseDsn(config.sentry.dsn) : null;
const enabled = Boolean(dsn);

if (config.sentry.dsn && !enabled) {
  console.warn('warning: SENTRY_DSN is set but could not be parsed — error reporting disabled');
}

// Turn a V8 stack string into Sentry frames (Sentry expects them newest-last).
function stackFrames(stack) {
  const frames = [];
  for (const line of String(stack || '').split('\n').slice(1)) {
    const m = /at (?:(.+?) \()?(.+?):(\d+):(\d+)\)?$/.exec(line.trim());
    if (!m) continue;
    const filename = m[2];
    frames.push({
      function: m[1] || '?',
      filename,
      lineno: Number(m[3]),
      colno: Number(m[4]),
      in_app: !filename.includes('node_modules') && !filename.startsWith('node:'),
    });
  }
  return frames.reverse();
}

function buildEvent(err, context) {
  const event = {
    event_id: crypto.randomBytes(16).toString('hex'),
    timestamp: Date.now() / 1000,
    platform: 'node',
    level: 'error',
    logger: config.app.name,
    server_name: os.hostname(),
    release: config.app.version,
    environment: config.sentry.environment,
    exception: {
      values: [{
        type: err.name || 'Error',
        value: err.message || String(err),
        stacktrace: { frames: stackFrames(err.stack) },
      }],
    },
  };
  if (context.request) event.request = context.request;
  if (context.tags) event.tags = context.tags;
  return event;
}

// Fire-and-forget POST. Reporting must never crash or stall the app.
function send(envelope) {
  const req = https.request({
    method: 'POST',
    hostname: dsn.hostname,
    port: dsn.port,
    path: dsn.path,
    headers: {
      'content-type': 'application/x-sentry-envelope',
      'content-length': Buffer.byteLength(envelope),
      'x-sentry-auth':
        `Sentry sentry_version=7, sentry_client=sctelemetry-web/${config.app.version}, sentry_key=${dsn.publicKey}`,
    },
  }, (res) => res.resume());   // drain and ignore the response
  req.on('error', (e) => console.error('sentry: send failed:', e.message));
  req.setTimeout(5000, () => req.destroy());
  req.end(envelope);
}

function captureException(err, context = {}) {
  if (!enabled || err == null) return;
  try {
    const event = buildEvent(err instanceof Error ? err : new Error(String(err)), context);
    const envelope =
      JSON.stringify({ event_id: event.event_id, sent_at: new Date().toISOString(), dsn: config.sentry.dsn }) + '\n' +
      JSON.stringify({ type: 'event' }) + '\n' +
      JSON.stringify(event) + '\n';
    send(envelope);
  } catch (e) {
    console.error('sentry: capture failed:', e.message);
  }
}

module.exports = { enabled, captureException };
