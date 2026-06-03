# ==========================================
# ETAPA 1: COMPILACIÓN DEL FRONTEND (REACT)
# ==========================================
FROM node:18-alpine AS frontend-build
WORKDIR /app
COPY WebhookListener/dashboard/package*.json ./
RUN npm install
COPY WebhookListener/dashboard/ ./
RUN npm run build

# ==========================================
# ETAPA 2: COMPILACIÓN DEL BACKEND (.NET 10)
# ==========================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src
COPY ["WebhookListener/WebhookListener.csproj", "WebhookListener/"]
RUN dotnet restore "WebhookListener/WebhookListener.csproj"
COPY WebhookListener/ ./WebhookListener/

# Inyectar los archivos estáticos de React compilados en el wwwroot de .NET
COPY --from=frontend-build /app/dist ./WebhookListener/wwwroot

WORKDIR "/src/WebhookListener"
RUN dotnet publish "WebhookListener.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ==========================================
# ETAPA 3: EJECUCIÓN (RUNTIME ULTRA-LIVIANO)
# ==========================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=backend-build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "WebhookListener.dll"]
