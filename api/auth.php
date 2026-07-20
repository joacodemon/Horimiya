<?php
/**
 * Horimiya License Server — Client Authentication Endpoint
 * 
 * POST /api/auth.php
 * Body: { "hwid": "XXXXXXXX-XXXXXXXX-XXXXXXXX-XXXXXXXX", "license_key": "HMRYA-XXXXX-XXXXX-XXXXX-XXXXX" }
 * 
 * Returns JSON:
 * { "success": true/false, "username": "...", "license_type": "...", "expires_at": "...", "message": "...", "hwid_bound": false }
 */

header('Content-Type: application/json; charset=utf-8');

// Only accept POST
if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
    http_response_code(405);
    echo json_encode(['success' => false, 'message' => 'Method not allowed']);
    exit;
}

require_once __DIR__ . '/db.php';

try {
    // Parse JSON body
    $rawInput = file_get_contents('php://input');
    $data = json_decode($rawInput, true);

    if (!$data || empty($data['hwid']) || empty($data['license_key'])) {
        echo json_encode(['success' => false, 'message' => 'Missing hwid or license_key']);
        exit;
    }

    $hwid       = trim($data['hwid']);
    $licenseKey = strtoupper(trim($data['license_key']));
    $ip         = $_SERVER['REMOTE_ADDR'] ?? 'unknown';

    // Hash the key and HWID for DB lookup
    $keyHash  = hash('sha256', $licenseKey);
    $hwidHash = hash('sha256', $hwid);

    $db = new LicenseDB();

    // 1. Find the license
    $license = $db->findByKeyHash($keyHash);

    if (!$license) {
        $db->logAuth(0, $hwidHash, $ip, false, 'Invalid key');
        echo json_encode([
            'success' => false,
            'message' => 'Invalid license key.'
        ]);
        exit;
    }

    $licenseId = (int)$license['id'];

    // 2. Check if active
    if (!(int)$license['is_active']) {
        $db->logAuth($licenseId, $hwidHash, $ip, false, 'Revoked');
        echo json_encode([
            'success' => false,
            'message' => 'This license has been revoked. Contact admin.'
        ]);
        exit;
    }

    // 3. Check expiry (lifetime = expires_at is NULL)
    if ($license['expires_at'] !== null) {
        $expiresTs = strtotime($license['expires_at']);
        if ($expiresTs !== false && time() > $expiresTs) {
            $db->logAuth($licenseId, $hwidHash, $ip, false, 'Expired');
            echo json_encode([
                'success'      => false,
                'message'      => 'License expired on ' . date('M d, Y', $expiresTs) . '. Contact admin to renew.',
                'license_type' => $license['license_type'],
                'expires_at'   => $license['expires_at'],
            ]);
            exit;
        }
    }

    // 4. HWID check / bind
    $hwidBound = false;
    if (empty($license['hwid_hash'])) {
        // First time — bind HWID
        $db->bindHwid($licenseId, $hwidHash);
        $hwidBound = true;
    } else if ($license['hwid_hash'] !== $hwidHash) {
        // HWID mismatch
        $db->logAuth($licenseId, $hwidHash, $ip, false, 'HWID mismatch');
        echo json_encode([
            'success' => false,
            'message' => 'HWID mismatch. This key is bound to another computer. Contact admin to reset.'
        ]);
        exit;
    }

    // 5. All checks passed — authenticate
    $db->touchAuth($licenseId);
    $db->logAuth($licenseId, $hwidHash, $ip, true, 'OK');

    echo json_encode([
        'success'      => true,
        'username'     => $license['username'],
        'license_type' => $license['license_type'],
        'expires_at'   => $license['expires_at'] ?? '',
        'message'      => 'Authenticated successfully.',
        'hwid_bound'   => $hwidBound,
    ]);

} catch (Exception $e) {
    http_response_code(500);
    echo json_encode([
        'success' => false,
        'message' => 'Server error. Try again later.'
    ]);
    // Log error to file for debugging
    error_log('Horimiya Auth Error: ' . $e->getMessage());
}
