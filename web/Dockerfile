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
# The base image ships its own default index.html/50x.html here; COPY merges into an existing
# directory rather than replacing it, so those stock files would otherwise survive alongside our
# build output. Harmless for APP=web (dist/index.html overwrites the same filename), but for
# APP=admin — whose build only emits admin.html — the stock index.html would stay reachable at
# /index.html and, since it exists, `try_files $uri /admin.html` would serve nginx's stock welcome
# page there instead of falling through to the SPA. Clear the directory first so only our build
# output (Task 7 gate-4: `ls` must show admin.html and no leftover index.html) is served.
# Base image's default html dir/files are root-owned (nginx-unprivileged still builds as root;
# only its final USER 101 is unprivileged) — switch to root for the rm + COPY, then back to the
# image's own unprivileged user so the running container keeps its non-root posture.
USER root
RUN rm -rf /usr/share/nginx/html/*
COPY --from=build /out /usr/share/nginx/html
# Template (not a static conf): the base image runs envsubst over /etc/nginx/templates/*.template
# at start, injecting ${CSP_EXTRA_ORIGINS} into the CSP header. See default.conf.template / admin.conf.template.
COPY --from=build /out.conf.template /etc/nginx/templates/default.conf.template
# Defined-empty default so an undefined ${CSP_EXTRA_ORIGINS} never leaks verbatim into the CSP
# header (envsubst only substitutes DEFINED env vars and has no ${VAR:-default} syntax). Real
# deployments (compose, Helm) override this with the browser-facing API + KeyCloak origins.
# Empty substitutes cleanly to `connect-src 'self' ;`.
ENV CSP_EXTRA_ORIGINS=""
USER 101

EXPOSE 8080
