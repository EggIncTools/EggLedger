# syntax=docker/dockerfile:1.7-labs
FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.2 AS build
WORKDIR /src
COPY global.json nuget.config Directory.Build.props Directory.Packages.props .editorconfig EggLedger.slnx ./
COPY .config/ .config/
COPY --parents EggLedger.Domain/*.csproj EggLedger.Web/*.csproj EggLedger.Web.Server/*.csproj EggLedger.Desktop/*.csproj ./
RUN --mount=type=secret,id=github_token \
    --mount=type=cache,target=/root/.nuget/packages \
    dotnet nuget update source github \
      --username EggIncTools \
      --password "$(cat /run/secrets/github_token)" \
      --store-password-in-clear-text \
      --configfile nuget.config \
    && dotnet restore EggLedger.Web.Server/EggLedger.Web.Server.csproj
COPY EggLedger.Domain/ EggLedger.Domain/
COPY EggLedger.Web/ EggLedger.Web/
COPY EggLedger.Web.Server/ EggLedger.Web.Server/
COPY EggLedger.Desktop/ EggLedger.Desktop/
ARG BUILD_HEIGHT
ARG BUILD_SHA
RUN --mount=type=cache,target=/root/.nuget/packages \
    BUILD_HEIGHT="$BUILD_HEIGHT" BUILD_SHA="$BUILD_SHA" \
    dotnet publish EggLedger.Web.Server/EggLedger.Web.Server.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:11.0.0-rc.2 AS runtime
ARG BUILD_HEIGHT
ARG BUILD_SHA
ENV GIT_SHA=$BUILD_SHA
ENV APP_VERSION=${BUILD_HEIGHT}.0.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build --chown=app:app /app ./
USER app
ENTRYPOINT ["dotnet", "EggLedger.Web.Server.dll"]
