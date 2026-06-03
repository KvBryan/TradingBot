# ==========================================
# ETAPA 1: COMPILACIÓN (BUILD)
# ==========================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copiar archivo de proyecto y restaurar dependencias
COPY ["WebhookListener/WebhookListener.csproj", "WebhookListener/"]
RUN dotnet restore "WebhookListener/WebhookListener.csproj"

# Copiar el resto del código y compilar la aplicación
COPY . .
WORKDIR "/src/WebhookListener"
RUN dotnet publish "WebhookListener.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ==========================================
# ETAPA 2: EJECUCIÓN (RUNTIME)
# ==========================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Exponer el puerto por defecto de ASP.NET Core
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Comando de inicio del contenedor
ENTRYPOINT ["dotnet", "WebhookListener.dll"]
