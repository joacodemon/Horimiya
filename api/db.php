<?php
/**
 * Horimiya License Server — Database Layer
 * 
 * Provides a thin wrapper around SQLite for license management.
 */

require_once __DIR__ . '/config.php';

class LicenseDB
{
    private $pdo;

    public function __construct()
    {
        $dbDir = dirname(DB_PATH);
        if (!is_dir($dbDir)) {
            mkdir($dbDir, 0755, true);
        }

        $this->pdo = new PDO('sqlite:' . DB_PATH);
        $this->pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
        $this->pdo->exec('PRAGMA journal_mode=WAL');
        $this->pdo->exec('PRAGMA foreign_keys=ON');
    }

    /**
     * Initialize the database schema. Safe to call multiple times.
     */
    public function createTables()
    {
        $this->pdo->exec("
            CREATE TABLE IF NOT EXISTS licenses (
                id               INTEGER PRIMARY KEY AUTOINCREMENT,
                license_key_hash TEXT UNIQUE NOT NULL,
                license_key_display TEXT NOT NULL,
                username         TEXT NOT NULL DEFAULT '',
                hwid_hash        TEXT DEFAULT NULL,
                license_type     TEXT NOT NULL DEFAULT 'monthly',
                created_at       TEXT NOT NULL DEFAULT (datetime('now')),
                expires_at       TEXT DEFAULT NULL,
                is_active        INTEGER NOT NULL DEFAULT 1,
                last_auth        TEXT DEFAULT NULL,
                notes            TEXT DEFAULT ''
            )
        ");

        $this->pdo->exec("
            CREATE TABLE IF NOT EXISTS auth_log (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                license_id  INTEGER,
                hwid_hash   TEXT,
                ip_address  TEXT,
                success     INTEGER,
                message     TEXT,
                created_at  TEXT NOT NULL DEFAULT (datetime('now'))
            )
        ");
    }

    /**
     * Find a license by its hashed key.
     */
    public function findByKeyHash(string $keyHash): ?array
    {
        $stmt = $this->pdo->prepare("SELECT * FROM licenses WHERE license_key_hash = :kh LIMIT 1");
        $stmt->execute([':kh' => $keyHash]);
        $row = $stmt->fetch(PDO::FETCH_ASSOC);
        return $row ?: null;
    }

    /**
     * Bind an HWID to a license (first use).
     */
    public function bindHwid(int $id, string $hwidHash): void
    {
        $stmt = $this->pdo->prepare("UPDATE licenses SET hwid_hash = :hh WHERE id = :id");
        $stmt->execute([':hh' => $hwidHash, ':id' => $id]);
    }

    /**
     * Update the last_auth timestamp for a license.
     */
    public function touchAuth(int $id): void
    {
        $stmt = $this->pdo->prepare("UPDATE licenses SET last_auth = datetime('now') WHERE id = :id");
        $stmt->execute([':id' => $id]);
    }

    /**
     * Log an authentication attempt.
     */
    public function logAuth(int $licenseId, string $hwidHash, string $ip, bool $success, string $message): void
    {
        $stmt = $this->pdo->prepare("
            INSERT INTO auth_log (license_id, hwid_hash, ip_address, success, message)
            VALUES (:lid, :hh, :ip, :s, :m)
        ");
        $stmt->execute([
            ':lid' => $licenseId,
            ':hh'  => $hwidHash,
            ':ip'  => $ip,
            ':s'   => $success ? 1 : 0,
            ':m'   => $message,
        ]);
    }

    /**
     * Create a new license key.
     * Returns the raw (unhashed) key string.
     */
    public function createLicense(string $username, string $licenseType, ?string $notes = ''): string
    {
        // Generate random key: HMRYA-XXXXX-XXXXX-XXXXX-XXXXX
        $chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'; // no ambiguous chars (0/O, 1/I)
        $segments = [];
        for ($s = 0; $s < 4; $s++) {
            $seg = '';
            for ($c = 0; $c < 5; $c++) {
                $seg .= $chars[random_int(0, strlen($chars) - 1)];
            }
            $segments[] = $seg;
        }
        $rawKey = KEY_PREFIX . '-' . implode('-', $segments);
        $keyHash = hash('sha256', $rawKey);
        $keyDisplay = KEY_PREFIX . '-' . substr($segments[0], 0, 3) . '**';

        // Calculate expiry
        $durations = LICENSE_DURATIONS;
        $days = isset($durations[$licenseType]) ? $durations[$licenseType] : 30;
        $expiresAt = null;
        if ($days > 0) {
            $expiresAt = date('Y-m-d H:i:s', time() + ($days * 86400));
        }

        $stmt = $this->pdo->prepare("
            INSERT INTO licenses (license_key_hash, license_key_display, username, license_type, expires_at, notes)
            VALUES (:kh, :kd, :u, :lt, :ea, :n)
        ");
        $stmt->execute([
            ':kh' => $keyHash,
            ':kd' => $keyDisplay,
            ':u'  => $username,
            ':lt' => $licenseType,
            ':ea' => $expiresAt,
            ':n'  => $notes ?? '',
        ]);

        return $rawKey;
    }

    /**
     * Get all licenses (for admin panel).
     */
    public function listAll(): array
    {
        $stmt = $this->pdo->query("SELECT id, license_key_display, username, hwid_hash, license_type, created_at, expires_at, is_active, last_auth, notes FROM licenses ORDER BY id DESC");
        return $stmt->fetchAll(PDO::FETCH_ASSOC);
    }

    /**
     * Revoke (deactivate) a license by ID.
     */
    public function revoke(int $id): bool
    {
        $stmt = $this->pdo->prepare("UPDATE licenses SET is_active = 0 WHERE id = :id");
        $stmt->execute([':id' => $id]);
        return $stmt->rowCount() > 0;
    }

    /**
     * Reactivate a license by ID.
     */
    public function activate(int $id): bool
    {
        $stmt = $this->pdo->prepare("UPDATE licenses SET is_active = 1 WHERE id = :id");
        $stmt->execute([':id' => $id]);
        return $stmt->rowCount() > 0;
    }

    /**
     * Reset the HWID for a license (e.g., user changed PC).
     */
    public function resetHwid(int $id): bool
    {
        $stmt = $this->pdo->prepare("UPDATE licenses SET hwid_hash = NULL WHERE id = :id");
        $stmt->execute([':id' => $id]);
        return $stmt->rowCount() > 0;
    }

    /**
     * Delete a license by ID.
     */
    public function deleteLicense(int $id): bool
    {
        $stmt = $this->pdo->prepare("DELETE FROM licenses WHERE id = :id");
        $stmt->execute([':id' => $id]);
        return $stmt->rowCount() > 0;
    }

    /**
     * Get recent auth logs.
     */
    public function getAuthLogs(int $limit = 50): array
    {
        $stmt = $this->pdo->prepare("
            SELECT al.*, l.username, l.license_key_display 
            FROM auth_log al 
            LEFT JOIN licenses l ON al.license_id = l.id 
            ORDER BY al.id DESC 
            LIMIT :lim
        ");
        $stmt->bindValue(':lim', $limit, PDO::PARAM_INT);
        $stmt->execute();
        return $stmt->fetchAll(PDO::FETCH_ASSOC);
    }
}
