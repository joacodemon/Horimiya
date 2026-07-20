# Horimiya License Server — Deployment Guide

## Requirements
- PHP 7.4+ with SQLite3 extension (almost all hosting has this)
- HTTPS recommended (free with Let's Encrypt on most hosts)

## Quick Setup

### 1. Upload Files
Upload the entire `api/` folder to your hosting:
```
your-site.com/
└── api/
    ├── .htaccess      ← protects sensitive files
    ├── config.php     ← EDIT THIS FIRST
    ├── db.php         ← database layer
    ├── auth.php       ← client endpoint
    └── admin.php      ← admin endpoint
```

### 2. Configure
Edit `config.php` and change `ADMIN_SECRET`:
```php
define('ADMIN_SECRET', 'my-super-secret-random-string-change-me');
```

### 3. Initialize Database
```bash
curl "https://your-site.com/api/admin.php?action=create_db&secret=YOUR_SECRET"
```
Response: `{ "success": true, "message": "Database initialized successfully." }`

### 4. Update Client URL
In `src/Auth/AuthManager.cs`, change:
```csharp
public static string ApiUrl = "https://your-site.com/api/auth.php";
```

---

## Admin Commands (via curl, Postman, or browser)

### Create a License Key
```bash
curl -X POST "https://your-site.com/api/admin.php" \
  -H "X-Admin-Secret: YOUR_SECRET" \
  -d "action=create_key&username=Player123&license_type=monthly"
```
**Response:**
```json
{
  "success": true,
  "key": "HMRYA-AB3K9-X72MQ-P1L4W-NZ8RT",
  "username": "Player123",
  "license_type": "monthly",
  "duration": "30 days"
}
```
→ Send `HMRYA-AB3K9-X72MQ-P1L4W-NZ8RT` to the user via Discord DM.

### Available License Types
| Type | Duration |
|------|----------|
| `trial` | 3 days |
| `monthly` | 30 days |
| `quarterly` | 90 days |
| `biannual` | 180 days |
| `yearly` | 365 days |
| `lifetime` | Never expires |

### List All Licenses
```bash
curl "https://your-site.com/api/admin.php?action=list_keys&secret=YOUR_SECRET"
```

### Revoke a License
```bash
curl "https://your-site.com/api/admin.php?action=revoke_key&id=3&secret=YOUR_SECRET"
```

### Reactivate a License
```bash
curl "https://your-site.com/api/admin.php?action=activate_key&id=3&secret=YOUR_SECRET"
```

### Reset HWID (user changed PC)
```bash
curl "https://your-site.com/api/admin.php?action=reset_hwid&id=3&secret=YOUR_SECRET"
```

### Delete a License Permanently
```bash
curl "https://your-site.com/api/admin.php?action=delete_key&id=3&secret=YOUR_SECRET"
```

### View Auth Logs
```bash
curl "https://your-site.com/api/admin.php?action=auth_logs&limit=20&secret=YOUR_SECRET"
```

---

## Security Notes
- The `.htaccess` file blocks direct access to `licenses.db`, `config.php`, and `db.php`
- License keys are hashed with SHA-256 in the database (the raw key is never stored)
- HWID is also hashed with SHA-256
- Always use HTTPS in production
- Change `ADMIN_SECRET` to a strong random string
- The `data/` folder is created automatically and should NOT be in your web root if possible

## Hosting Recommendations
- **Free:** InfinityFree, 000webhost (both support PHP + SQLite)
- **Cheap ($2-5/mo):** Hostinger, Namecheap Stellar
- **VPS:** DigitalOcean ($4/mo), Hetzner ($3.5/mo)
