FROM node:22-alpine AS frontend
WORKDIR /frontend
COPY package.json package-lock.json ./
RUN npm ci
COPY scripts ./scripts
RUN npm run build:client

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
COPY --from=frontend /frontend/src/Quizizzo.Web/wwwroot/vendor/ src/Quizizzo.Web/wwwroot/vendor/
RUN dotnet restore src/Quizizzo.Web/Quizizzo.Web.csproj
RUN dotnet publish src/Quizizzo.Web/Quizizzo.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
RUN mkdir -p /app/assets/drawings /app/data-protection \
    && chown -R app:app /app/assets /app/data-protection
USER app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "Quizizzo.Web.dll"]
