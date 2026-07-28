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
//
//  Tipos disponibles al crear una key (admin panel / API):
//    perma    → Comprado permanente (sin expiración, cualquier PC)
//    lifetime → Owner/Dev (sin expiración, sin HWID-lock desde server)
//    30d      → Comprado 30 días
//    14d      → Trial 14 días
//    7d       → Trial 7 días
//    monthly  → Alias de 30d (legado)
//    quarterly → 90 días
//    biannual → 180 días
//    yearly   → 365 días
//
define('LICENSE_DURATIONS', [
    // ── Compradores ───────────────────────────────────────────────────────────
    'perma'     => 0,    // Sin expiración — comprado permanente
    '30d'       => 30,   // 30 días — comprado mensual

    // ── Trials ────────────────────────────────────────────────────────────────
    '14d'       => 14,   // 14 días de prueba
    '7d'        => 7,    // 7 días de prueba

    // ── Legado / Extras ───────────────────────────────────────────────────────
    'monthly'   => 30,
    'quarterly' => 90,
    'biannual'  => 180,
    'yearly'    => 365,
    'lifetime'  => 0,    // 0 = nunca expira
]);

// ── CORS (optional — enable if client needs it) ──────────────────────────────
// header('Access-Control-Allow-Origin: *');
// header('Access-Control-Allow-Methods: POST, GET');
// header('Access-Control-Allow-Headers: Content-Type, X-Admin-Secret');
