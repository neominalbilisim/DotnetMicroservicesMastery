# =====================================================================
# Modül 1 - "Container Teknolojisi (Dockerize)": Multi-stage Dockerfile
# Build context: repo kökü (docker-compose.yml buna göre ayarlıdır)
# =====================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Services/Saga/Saga.Api/Saga.Api.csproj", "src/Services/Saga/Saga.Api/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Common/BuildingBlocks.Common.csproj", "src/BuildingBlocks/BuildingBlocks.Common/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Observability/BuildingBlocks.Observability.csproj", "src/BuildingBlocks/BuildingBlocks.Observability/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Security/BuildingBlocks.Security.csproj", "src/BuildingBlocks/BuildingBlocks.Security/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Messaging/BuildingBlocks.Messaging.csproj", "src/BuildingBlocks/BuildingBlocks.Messaging/"]
RUN dotnet restore "src/Services/Saga/Saga.Api/Saga.Api.csproj"

COPY . .
WORKDIR "/src/src/Services/Saga/Saga.Api"
RUN dotnet publish "Saga.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Saga.Api.dll"]
