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
RUN dotnet restore Quizizzo.sln
RUN dotnet publish src/Quizizzo.Web/Quizizzo.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "Quizizzo.Web.dll"]
