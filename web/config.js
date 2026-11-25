'use strict';

// Configuration from environment, with a tiny .env loader (no dependency).

const fs = require('node:fs');
const path = require('node:path');

function loadEnvFile(file) {
  try {
    for (const line of fs.readFileSync(file, 'utf8').split('\n')) {
      const s = line.trim();
      if (!s || s[0] === '#') continue;
      const eq = s.indexOf('=');
      if (eq <= 0) continue;
      const key = s.slice(0, eq).trim();
      let val = s.slice(eq + 1).trim();
      if (val.length >= 2 && (val[0] === '"' || val[0] === "'") && val.at(-1) === val[0]) val = val.slice(1, -1);
      if (!(key in process.env)) process.env[key] = val;
    }
  } catch { /* no .env file is fine */ }
}

loadEnvFile(path.join(__dirname, '.env'));

const config = {
  host: process.env.HOST || '0.0.0.0',
  port: Number(process.env.PORT) || 8080,
  baseUrl: (process.env.BASE_URL || 'http://localhost:8080').replace(/\/$/, ''),
  dataFile: process.env.DATA_FILE || path.join(__dirname, 'data', 'data.json'),
  sessionSecret: process.env.SESSION_SECRET || '',
  defaultHistory: Number(process.env.DEFAULT_HISTORY) || 100,
  maxHistory: Number(process.env.MAX_HISTORY) || 1000,
  discord: {
    clientId: process.env.DISCORD_CLIENT_ID || '',
    clientSecret: process.env.DISCORD_CLIENT_SECRET || '',
  },
  // Error reporting. Configured only here / via .env — there is no UI for it.
  sentry: {
    dsn: process.env.SENTRY_DSN || '',
    environment: process.env.SENTRY_ENVIRONMENT || 'production',
  },
  // Dev-only login bypass (no Discord). Never enable in production.
  allowDevLogin: process.env.ALLOW_DEV_LOGIN === '1',
};

config.app = {
  name: 'SCTelemetry Web',
  tagline: 'Telemetry collector for Star Citizen',
  version: require('./package.json').version,
  repo: 'https://github.com/scpages/telemetry',
};

config.apiPrefix = '/api/v1';   // versioned API base; legacy /api/* kept as aliases
config.redirectUri = config.baseUrl + '/auth/discord/callback';
config.discordConfigured = Boolean(config.discord.clientId && config.discord.clientSecret);
config.sentryConfigured = Boolean(config.sentry.dsn);

if (!config.sessionSecret) {
  // Ephemeral secret: sessions won't survive a restart, but the app still runs.
  config.sessionSecret = require('node:crypto').randomBytes(32).toString('hex');
  console.warn('warning: SESSION_SECRET not set — using an ephemeral one (logins drop on restart)');
}

module.exports = config;
