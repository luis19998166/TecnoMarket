#!/usr/bin/env bash
# Uso: blue-green.sh <imagen>. Requiere nginx con upstream en /etc/nginx/conf.d/upstream.conf
set -euo pipefail
IMG="$1"
ACTIVO=$(cat /opt/app/activo 2>/dev/null || echo blue)
if [ "$ACTIVO" = blue ]; then NUEVO=green; PUERTO=8082; else NUEVO=blue; PUERTO=8081; fi

docker rm -f "app-$NUEVO" 2>/dev/null || true
docker run -d --name "app-$NUEVO" -p "$PUERTO:8080" --restart unless-stopped "$IMG"

# 1) health check  2) prueba de humo de negocio (compuerta)
for i in $(seq 1 60); do curl -fs "http://localhost:$PUERTO/healthz" && break; sleep 1; done
curl -fs "http://localhost:$PUERTO/api/pedidos/1" > /dev/null || { echo "Smoke test fallido: se aborta"; docker rm -f "app-$NUEVO"; exit 1; }

# 3) conmutacion atomica del trafico
echo "upstream blazor { server 127.0.0.1:$PUERTO; }" > /etc/nginx/conf.d/upstream.conf
nginx -t && nginx -s reload
echo "$NUEVO" > /opt/app/activo
# La version anterior se conserva encendida como reversion inmediata:
#   ./rollback.sh  -> reescribe upstream.conf al puerto anterior y recarga nginx
