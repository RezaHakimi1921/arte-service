#!/usr/bin/env bash
# Run from the repo root on the dev machine (Git Bash): builds the PWA, uploads HEAD + web/dist,
# rebuilds the API on the server, restarts, and checks health. Commit first: only HEAD is shipped.
set -euo pipefail
KEY=~/.ssh/arte_service_deploy
HOST=cluadai@78.39.51.105
SSH="ssh -i $KEY -o IdentitiesOnly=yes -p 2238 $HOST"

(cd web && npm run build >/dev/null)
TAR=/tmp/arte-ship.tar
git archive --format=tar HEAD -o $TAR
tar --force-local -rf $TAR web/dist
scp -q -i $KEY -o IdentitiesOnly=yes -P 2238 $TAR $HOST:arte-service/arte.tar
rm -f $TAR

$SSH 'set -e; cd ~/arte-service && tar -xf arte.tar && rm arte.tar && cd deploy \
  && docker compose build api >/dev/null && docker compose up -d >/dev/null && docker compose restart web >/dev/null \
  && sleep 10 && docker compose ps --format "{{.Service}} {{.Status}}" \
  && docker compose logs api --since 2m 2>&1 | grep -E "Applying migration|fail:|Exception" | cut -c1-160 || true'

curl -s -o /dev/null -w "site %{http_code}\n" https://service.artepersia.com/
curl -s -o /dev/null -w "api (unauthenticated) %{http_code}\n" https://service.artepersia.com/api/v1/me
