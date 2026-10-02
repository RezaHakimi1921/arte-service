#!/usr/bin/env sh
# Daily database dump, kept 14 days. Install with: crontab -e  →  15 3 * * * /opt/arte-service/deploy/backup.sh
set -eu
cd "$(dirname "$0")"
mkdir -p backups
umask 077
docker compose exec -T db pg_dump -U arte -d arte -Fc > "backups/arte-$(date +%Y%m%d-%H%M).dump"
find backups -name 'arte-*.dump' -mtime +14 -delete
