#!/usr/bin/env sh
# Daily database dump, kept 14 days. Install with: crontab -e  →  15 3 * * * /home/cluadai/arte-service/deploy/backup.sh
set -eu
cd "$(dirname "$0")"
mkdir -p backups
umask 077
docker compose exec -T db pg_dump -U arte -d arte -Fc > "backups/arte-$(date +%Y%m%d-%H%M).dump"
docker run --rm -v arte-service_uploads:/u:ro -v "$(pwd)/backups":/b alpine tar czf "/b/uploads-$(date +%Y%m%d-%H%M).tgz" -C /u .
find backups -name 'arte-*.dump' -mtime +14 -delete
find backups -name 'uploads-*.tgz' -mtime +14 -delete
