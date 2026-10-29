# One image definition for both processes: docker build --build-arg PROJECT=NotificationService.Api (or .Worker)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props NotificationService.slnx ./
COPY src/ src/
RUN dotnet publish "src/${PROJECT}/${PROJECT}.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG PROJECT
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    APP_DLL=${PROJECT}.dll
EXPOSE 8080

COPY --from=build /app/publish .

USER $APP_UID

ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_DLL\""]
