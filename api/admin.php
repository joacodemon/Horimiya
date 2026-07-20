<?php
/**
 * Horimiya License Server — Admin API
 * 
 * All requests must include the admin secret as:
 *   - Header:  X-Admin-Secret: YOUR_SECRET
 *   - or GET:  ?secret=YOUR_SECRET
 * 
 * Actions (via POST/GET param "action"):
 *   create_db      — Initialize the database (run once)
 *   create_key     — Create a new license key
 *   list_keys      — List all licenses
 *   revoke_key     — Deactivate a license
 *   activate_key   — Reactivate a license
 *   reset_hwid     — Reset HWID binding
 *   delete_key     — Permanently delete a license
 *   auth_logs      — View recent auth attempts
 */

header('Content-Type: application/json; charset=utf-8');

require_once __DIR__ . '/db.php';

// ── Authenticate Admin ───────────────────────────────────────────────────────
$secret = '';
if (isset($_SERVER['HTTP_X_ADMIN_SECRET'])) {
    $secret = $_SERVER['HTTP_X_ADMIN_SECRET'];
} elseif (isset($_REQUEST['secret'])) {
    $secret = $_REQUEST['secret'];
}

if ($secret !== ADMIN_SECRET) {
    http_response_code(403);
    echo json_encode(['success' => false, 'message' => 'Unauthorized. Invalid admin secret.']);
    exit;
}

// ── Route Action ─────────────────────────────────────────────────────────────
$action = $_REQUEST['action'] ?? '';

try {
    $db = new LicenseDB();

    switch ($action) {

        // ── Initialize Database ──────────────────────────────────────────
        case 'create_db':
            $db->createTables();
            echo json_encode(['success' => true, 'message' => 'Database initialized successfully.']);
            break;

        // ── Create License Key ───────────────────────────────────────────
        case 'create_key':
            $username    = trim($_REQUEST['username']    ?? '');
            $licenseType = trim($_REQUEST['license_type'] ?? 'monthly');
            $notes       = trim($_REQUEST['notes']       ?? '');

            if (empty($username)) {
                echo json_encode(['success' => false, 'message' => 'Username is required.']);
                exit;
            }

            $validTypes = array_keys(LICENSE_DURATIONS);
            if (!in_array($licenseType, $validTypes)) {
                echo json_encode([
                    'success' => false,
                    'message' => 'Invalid license_type. Valid: ' . implode(', ', $validTypes)
                ]);
                exit;
            }

            $rawKey = $db->createLicense($username, $licenseType, $notes);
            $days = LICENSE_DURATIONS[$licenseType];

            echo json_encode([
                'success'      => true,
                'key'          => $rawKey,
                'username'     => $username,
                'license_type' => $licenseType,
                'duration'     => $days > 0 ? "{$days} days" : 'lifetime',
                'message'      => "Key created for {$username}. Send them this key.",
            ]);
            break;

        // ── List All Licenses ────────────────────────────────────────────
        case 'list_keys':
            $licenses = $db->listAll();
            echo json_encode([
                'success'  => true,
                'count'    => count($licenses),
                'licenses' => $licenses,
            ]);
            break;

        // ── Revoke License ───────────────────────────────────────────────
        case 'revoke_key':
            $id = (int)($_REQUEST['id'] ?? 0);
            if ($id <= 0) {
                echo json_encode(['success' => false, 'message' => 'id is required.']);
                exit;
            }
            $ok = $db->revoke($id);
            echo json_encode([
                'success' => $ok,
                'message' => $ok ? "License #{$id} revoked." : "License #{$id} not found.",
            ]);
            break;

        // ── Activate License ─────────────────────────────────────────────
        case 'activate_key':
            $id = (int)($_REQUEST['id'] ?? 0);
            if ($id <= 0) {
                echo json_encode(['success' => false, 'message' => 'id is required.']);
                exit;
            }
            $ok = $db->activate($id);
            echo json_encode([
                'success' => $ok,
                'message' => $ok ? "License #{$id} activated." : "License #{$id} not found.",
            ]);
            break;

        // ── Reset HWID ───────────────────────────────────────────────────
        case 'reset_hwid':
            $id = (int)($_REQUEST['id'] ?? 0);
            if ($id <= 0) {
                echo json_encode(['success' => false, 'message' => 'id is required.']);
                exit;
            }
            $ok = $db->resetHwid($id);
            echo json_encode([
                'success' => $ok,
                'message' => $ok ? "HWID reset for license #{$id}. User can bind a new PC." : "License #{$id} not found.",
            ]);
            break;

        // ── Delete License ───────────────────────────────────────────────
        case 'delete_key':
            $id = (int)($_REQUEST['id'] ?? 0);
            if ($id <= 0) {
                echo json_encode(['success' => false, 'message' => 'id is required.']);
                exit;
            }
            $ok = $db->deleteLicense($id);
            echo json_encode([
                'success' => $ok,
                'message' => $ok ? "License #{$id} deleted permanently." : "License #{$id} not found.",
            ]);
            break;

        // ── Auth Logs ────────────────────────────────────────────────────
        case 'auth_logs':
            $limit = (int)($_REQUEST['limit'] ?? 50);
            $logs = $db->getAuthLogs($limit);
            echo json_encode([
                'success' => true,
                'count'   => count($logs),
                'logs'    => $logs,
            ]);
            break;

        // ── Unknown Action ───────────────────────────────────────────────
        default:
            echo json_encode([
                'success' => false,
                'message' => 'Unknown action. Available: create_db, create_key, list_keys, revoke_key, activate_key, reset_hwid, delete_key, auth_logs',
            ]);
            break;
    }

} catch (Exception $e) {
    http_response_code(500);
    echo json_encode([
        'success' => false,
        'message' => 'Server error: ' . $e->getMessage(),
    ]);
}
