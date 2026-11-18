#!/bin/sh
# Starts every part of the demo on local ports behind nginx (8080, the port Render routes to): RabbitMQ and Mailpit
# first, then the API (which migrates the database) and a worker, then nginx.
set -eu

export HOME=/tmp/home
mkdir -p "$HOME" /tmp/rabbitmq/log /tmp/rabbitmq/mnesia

# Workstation garbage collection and a smaller heap: three runtimes share the free plan's memory.
export DOTNET_gcServer=0 DOTNET_GCConserveMemory=7

RABBITMQ_CONFIG_FILE=/app/rabbitmq.conf \
RABBITMQ_LOG_BASE=/tmp/rabbitmq/log \
RABBITMQ_MNESIA_BASE=/tmp/rabbitmq/mnesia \
RABBITMQ_ENABLED_PLUGINS_FILE=/tmp/rabbitmq/enabled_plugins \
RABBITMQ_NODENAME=rabbit@localhost \
RABBITMQ_SERVER_ADDITIONAL_ERL_ARGS="+S 1:1 +sbwt none +sbwtdcpu none +sbwtdio none +MMmcs 30" \
  /usr/lib/rabbitmq/bin/rabbitmq-server &

mailpit --smtp 127.0.0.1:1025 --listen 127.0.0.1:8025 --webroot /mail/ --max 500 &

# The worker sets up its queues when it starts, so RabbitMQ has to be listening first.
for _ in $(seq 1 120); do
  bash -c 'exec 3<>/dev/tcp/127.0.0.1/5672' 2>/dev/null && break
  sleep 1
done

export ConnectionStrings__RabbitMq="amqp://guest:guest@127.0.0.1:5672/"

cd /app/api
ASPNETCORE_HTTP_PORTS=8081 dotnet NotificationService.Api.dll &
api=$!

cd /app/worker
ASPNETCORE_HTTP_PORTS=8082 Smtp__Host=127.0.0.1 Smtp__Port=1025 dotnet NotificationService.Worker.dll &
worker=$!

nginx -e stderr -c /app/nginx.conf -g 'daemon off;' &
web=$!

trap 'kill 0' TERM INT
# The container lives as long as the API, the worker and nginx do.
wait "$api" "$worker" "$web"
