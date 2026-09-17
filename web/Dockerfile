# syntax=docker/dockerfile:1.7

# ─── build ───────────────────────────────────────────────────────────────
FROM node:20-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci --ignore-scripts

COPY . .
RUN npm run build

# ─── runtime ─────────────────────────────────────────────────────────────
FROM nginxinc/nginx-unprivileged:1.27-alpine AS runtime
COPY --from=build /app/dist /usr/share/nginx/html
# Template (not a static conf): the base image runs envsubst over /etc/nginx/templates/*.template
# at start, injecting ${CSP_EXTRA_ORIGINS} into the CSP header. See default.conf.template.
COPY default.conf.template /etc/nginx/templates/default.conf.template
# Defined-empty default so an undefined ${CSP_EXTRA_ORIGINS} never leaks verbatim into the CSP
# header (envsubst only substitutes DEFINED env vars and has no ${VAR:-default} syntax). Real
# deployments (compose, and any future k8s manifest) override this with the browser-facing API +
# KeyCloak origins. Empty substitutes cleanly to `connect-src 'self' ;`.
ENV CSP_EXTRA_ORIGINS=""

EXPOSE 8080
