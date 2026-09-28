# syntax=docker/dockerfile:1.7

# APP selects which entry of this project the image serves (ADR-0118, amended 2026-09-28):
#   web   — tenant SPA (index.html → dist/, CSP default.conf.template)
#   admin — platform-operator console (admin.html → dist-admin/, CSP admin.conf.template)
ARG APP=web

# ─── build ───────────────────────────────────────────────────────────────
FROM node:20-alpine AS build
ARG APP
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci --ignore-scripts

COPY . .
RUN case "$APP" in \
      web)   npm run build       && mv dist /out       && cp default.conf.template /out.conf.template ;; \
      admin) npm run build:admin && mv dist-admin /out && cp admin.conf.template /out.conf.template ;; \
      *)     echo "unknown APP=$APP (expected web|admin)" >&2 && exit 1 ;; \
    esac

# ─── runtime ─────────────────────────────────────────────────────────────
FROM nginxinc/nginx-unprivileged:1.27-alpine AS runtime
# Serve from a dedicated dir the base image never populates (avoids stock index.html/50x.html
# surviving a merge-COPY into /usr/share/nginx/html; root points here in the conf templates too).
COPY --from=build /out /usr/share/nginx/app
# Template (not a static conf): the base image runs envsubst over /etc/nginx/templates/*.template
# at start, injecting ${CSP_EXTRA_ORIGINS} into the CSP header. See default.conf.template / admin.conf.template.
COPY --from=build /out.conf.template /etc/nginx/templates/default.conf.template
# Defined-empty default so an undefined ${CSP_EXTRA_ORIGINS} never leaks verbatim into the CSP
# header (envsubst only substitutes DEFINED env vars and has no ${VAR:-default} syntax). Real
# deployments (compose, Helm) override this with the browser-facing API + KeyCloak origins.
# Empty substitutes cleanly to `connect-src 'self' ;`.
ENV CSP_EXTRA_ORIGINS=""

EXPOSE 8080
