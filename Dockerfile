ARG NODE_IMAGE=node:22-bookworm-slim
ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
ARG DOTNET_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0

# Stage 1: Web frontend
FROM ${NODE_IMAGE} AS web
WORKDIR /repo/web
COPY web/ ./
RUN mkdir -p /repo/src/Shipit.Api/wwwroot && \
    if [ -f package.json ]; then \
        npm ci && npm run build; \
    fi

# Stage 2: .NET SDK build & publish
FROM ${DOTNET_SDK_IMAGE} AS build
WORKDIR /repo
COPY Shipit.sln global.json Directory.Build.props ./
COPY src/Shipit.Core/Shipit.Core.csproj src/Shipit.Core/
COPY src/Shipit.Data/Shipit.Data.csproj src/Shipit.Data/
COPY src/Shipit.Security/Shipit.Security.csproj src/Shipit.Security/
COPY src/Shipit.Ssh/Shipit.Ssh.csproj src/Shipit.Ssh/
COPY src/Shipit.K8s/Shipit.K8s.csproj src/Shipit.K8s/
COPY src/Shipit.Engine/Shipit.Engine.csproj src/Shipit.Engine/
COPY src/Shipit.Api/Shipit.Api.csproj src/Shipit.Api/
RUN dotnet restore src/Shipit.Api/Shipit.Api.csproj

COPY src/ ./src/
COPY --from=web /repo/src/Shipit.Api/wwwroot/. ./src/Shipit.Api/wwwroot/
RUN dotnet publish src/Shipit.Api/Shipit.Api.csproj -c Release -o /app --no-restore
COPY --from=web /repo/src/Shipit.Api/wwwroot/. /app/wwwroot/

# Stage 3: Runtime
FROM ${DOTNET_RUNTIME_IMAGE} AS final
WORKDIR /app
COPY --from=build /app ./
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Shipit.Api.dll"]
