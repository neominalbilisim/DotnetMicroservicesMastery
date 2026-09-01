# =====================================================================
# Modül 1 - "Container Teknolojisi (Dockerize)": Multi-stage Dockerfile
# Build (SDK, büyük) ve Runtime (küçük) ortamları ayrılarak hem imaj
# boyutu küçültülür hem de üretim ortamına build araçları sızmaz.
# Build context: repo kökü (docker-compose.yml buna göre ayarlıdır)
# =====================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Services/Order/Order.Api/Order.Api.csproj", "src/Services/Order/Order.Api/"]
COPY ["src/Services/Order/Order.Application/Order.Application.csproj", "src/Services/Order/Order.Application/"]
COPY ["src/Services/Order/Order.Domain/Order.Domain.csproj", "src/Services/Order/Order.Domain/"]
COPY ["src/Services/Order/Order.Infrastructure/Order.Infrastructure.csproj", "src/Services/Order/Order.Infrastructure/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Common/BuildingBlocks.Common.csproj", "src/BuildingBlocks/BuildingBlocks.Common/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Observability/BuildingBlocks.Observability.csproj", "src/BuildingBlocks/BuildingBlocks.Observability/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Resilience/BuildingBlocks.Resilience.csproj", "src/BuildingBlocks/BuildingBlocks.Resilience/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Messaging/BuildingBlocks.Messaging.csproj", "src/BuildingBlocks/BuildingBlocks.Messaging/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Security/BuildingBlocks.Security.csproj", "src/BuildingBlocks/BuildingBlocks.Security/"]
RUN dotnet restore "src/Services/Order/Order.Api/Order.Api.csproj"

COPY . .
WORKDIR "/src/src/Services/Order/Order.Api"
RUN dotnet publish "Order.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Order.Api.dll"]
