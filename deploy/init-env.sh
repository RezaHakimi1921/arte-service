#!/usr/bin/env sh
# Generates deploy/.env with fresh random secrets. Run once on the server; never commit .env.
set -eu
cd "$(dirname "$0")"
if [ -f .env ]; then echo ".env already exists; refusing to overwrite." >&2; exit 1; fi
umask 077
cat > .env <<ENV
POSTGRES_PASSWORD=$(openssl rand -hex 32)
JWT_KEY=$(openssl rand -base64 48 | tr -d '\n')
OTP_PEPPER=$(openssl rand -hex 32)
SIGNUP_INVITE_CODE=$(openssl rand -hex 8)
SMS_PROVIDER=fake
SMS_ALLOW_FAKE=true
SMS_FAKE_MOBILE_1=${1:-}
SMS_FAKE_MOBILE_2=
ENV
echo "Wrote $(pwd)/.env (mode 600)."
echo "Signup invite code: $(grep SIGNUP_INVITE_CODE .env | cut -d= -f2)"
