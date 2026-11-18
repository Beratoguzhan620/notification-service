# The public demo in one container, for a single free web service (Render) next to a hosted PostgreSQL (Neon):
# nginx with the demo page in front, the API, one worker, RabbitMQ and Mailpit, each on a local port. Compose runs
# them as separate containers, with two workers and Jaeger.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props NotificationService.slnx ./
COPY src/ src/
RUN dotnet publish src/NotificationService.Api/NotificationService.Api.csproj -c Release -o /app/api /p:UseAppHost=false \
 && dotnet publish src/NotificationService.Worker/NotificationService.Worker.csproj -c Release -o /app/worker /p:UseAppHost=false

FROM axllent/mailpit:v1.31.6 AS mail

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
 && apt-get install -y --no-install-recommends nginx rabbitmq-server \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/api ./api
COPY --from=build /app/worker ./worker
COPY --from=mail /mailpit /usr/local/bin/mailpit
COPY deploy/demo/web ./web
COPY deploy/demo/nginx.conf deploy/demo/rabbitmq.conf deploy/demo/start.sh ./
USER $APP_UID
ENTRYPOINT ["sh", "/app/start.sh"]
