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
# sms.ir: set SMS_PROVIDER=smsir once the OTP template is approved.
SMSIR_API_KEY=
SMSIR_OTP_TEMPLATE_ID=
SMSIR_OTP_PARAM=CODE
# SmartX SMS (docs/15). Secrets come from the SmartX back office; never paste them into chat or git.
# Send everything through SmartX: SMS_PROVIDER=smartx. Move single keys first: SMARTX_KEYS=case.opened,case.ready
SMARTX_BASE_URL=https://staging-api.smartx.ir
SMARTX_CLIENT_ID=
SMARTX_CLIENT_SECRET=
SMARTX_TENANT_ID=
SMARTX_SENDER_NUMBER=
SMARTX_KEYS=
# Bugsink error tracking: log in as admin@artepersia.com with the password after "admin@artepersia.com:".
BUGSINK_SECRET_KEY=$(openssl rand -hex 40)
BUGSINK_ADMIN=admin@artepersia.com:$(openssl rand -hex 12)
# Filled after creating the project in Bugsink (see CLAUDE.md, Server notes).
SENTRY_DSN=
ENV
echo "Wrote $(pwd)/.env (mode 600)."
echo "Signup invite code: $(grep SIGNUP_INVITE_CODE .env | cut -d= -f2)"
