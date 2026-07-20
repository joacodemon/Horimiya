<?php
/**
 * Horimiya License Server — Configuration
 * 
 * IMPORTANT: Change ADMIN_SECRET to your own random string before deploying.
 * Keep this file secure and never expose it publicly.
 */

// ── Admin Secret ─────────────────────────────────────────────────────────────
// Used to authenticate admin API requests (create keys, revoke, reset HWID, etc.)
// Change this to a long random string. Example: openssl rand -hex 32
define('ADMIN_SECRET', 'youngflexd1233');

// ── Database Path ────────────────────────────────────────────────────────────
// SQLite database file. Will be created automatically on first use.
// Make sure the directory is writable by PHP.
define('DB_PATH', __DIR__ . '/data/licenses.db');

// ── License Key Prefix ───────────────────────────────────────────────────────
define('KEY_PREFIX', 'HMRYA');

// ── License Duration Presets (in days) ───────────────────────────────────────
define('LICENSE_DURATIONS', [
    'trial'     => 7,
    'monthly'   => 30,
    'quartly' => 90,
    'biannual'  => 180,
    'yearly'    => 365,
    'lifetime'  => 0,   // 0 = never expires
]);

// ── CORS (optional — enable if client needs it) ──────────────────────────────
// header('Access-Control-Allow-Origin: *');
// header('Access-Control-Allow-Methods: POST, GET');
// header('Access-Control-Allow-Headers: Content-Type, X-Admin-Secret');
