{{/*
Expand the name of the chart.
*/}}
{{- define "kartova.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Create a default fully qualified app name.
*/}}
{{- define "kartova.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{/*
Chart label block.
*/}}
{{- define "kartova.labels" -}}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{ include "kartova.selectorLabels" . }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{- define "kartova.selectorLabels" -}}
app.kubernetes.io/name: {{ include "kartova.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{/*
SPA workload (nginx-unprivileged serving a Vite build) — shared by the tenant SPA (web) and the
platform-operator console (web-admin, ADR-0118). Call with (dict "root" $ "component" "web" "values" .Values.web).
*/}}
{{- define "kartova.spa.deployment" -}}
{{- $root := .root -}}
{{- $v := .values -}}
apiVersion: apps/v1
kind: Deployment
metadata:
  name: {{ include "kartova.fullname" $root }}-{{ .component }}
  labels:
    {{- include "kartova.labels" $root | nindent 4 }}
    app.kubernetes.io/component: {{ .component }}
spec:
  replicas: {{ $v.replicaCount }}
  selector:
    matchLabels:
      {{- include "kartova.selectorLabels" $root | nindent 6 }}
      app.kubernetes.io/component: {{ .component }}
  template:
    metadata:
      labels:
        {{- include "kartova.selectorLabels" $root | nindent 8 }}
        app.kubernetes.io/component: {{ .component }}
    spec:
      securityContext:
        runAsNonRoot: true
      containers:
        - name: {{ .component }}
          image: "{{ $root.Values.image.repository }}-{{ $v.image.name }}:{{ $root.Values.image.tag | default $root.Chart.AppVersion }}"
          imagePullPolicy: {{ $root.Values.image.pullPolicy }}
          ports:
            - name: http
              containerPort: {{ $v.port }}
              protocol: TCP
          env:
            - name: CSP_EXTRA_ORIGINS
              value: {{ $v.cspExtraOrigins | quote }}
            - name: KARTOVA_OIDC_AUTHORITY
              value: {{ $v.config.oidcAuthority | quote }}
            - name: KARTOVA_OIDC_CLIENT_ID
              value: {{ $v.config.oidcClientId | quote }}
            - name: KARTOVA_API_BASE_URL
              value: {{ $v.config.apiBaseUrl | quote }}
          livenessProbe:
            httpGet:
              path: /
              port: http
            periodSeconds: 10
          readinessProbe:
            httpGet:
              path: /
              port: http
            periodSeconds: 5
          resources:
            {{- toYaml $v.resources | nindent 12 }}
{{- end }}

{{- define "kartova.spa.service" -}}
{{- $root := .root -}}
apiVersion: v1
kind: Service
metadata:
  name: {{ include "kartova.fullname" $root }}-{{ .component }}
  labels:
    {{- include "kartova.labels" $root | nindent 4 }}
    app.kubernetes.io/component: {{ .component }}
spec:
  type: ClusterIP
  selector:
    {{- include "kartova.selectorLabels" $root | nindent 4 }}
    app.kubernetes.io/component: {{ .component }}
  ports:
    - name: http
      port: 80
      targetPort: http
      protocol: TCP
{{- end }}
