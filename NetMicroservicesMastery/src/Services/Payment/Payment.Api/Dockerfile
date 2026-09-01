# =====================================================================
# Modül 1 - "Container Teknolojisi (Dockerize)": Multi-stage Dockerfile
# Build (SDK, büyük) ve Runtime (küçük) ortamları ayrılarak hem imaj
# boyutu küçültülür hem de üretim ortamına build araçları sızmaz.
# Build context: repo kökü (docker-compose.yml buna göre ayarlıdır)
# =====================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Services/Payment/Payment.Api/Payment.Api.csproj", "src/Services/Payment/Payment.Api/"]
COPY ["src/Services/Payment/Payment.Application/Payment.Application.csproj", "src/Services/Payment/Payment.Application/"]
COPY ["src/Services/Payment/Payment.Domain/Payment.Domain.csproj", "src/Services/Payment/Payment.Domain/"]
COPY ["src/Services/Payment/Payment.Infrastructure/Payment.Infrastructure.csproj", "src/Services/Payment/Payment.Infrastructure/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Common/BuildingBlocks.Common.csproj", "src/BuildingBlocks/BuildingBlocks.Common/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Observability/BuildingBlocks.Observability.csproj", "src/BuildingBlocks/BuildingBlocks.Observability/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Resilience/BuildingBlocks.Resilience.csproj", "src/BuildingBlocks/BuildingBlocks.Resilience/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Messaging/BuildingBlocks.Messaging.csproj", "src/BuildingBlocks/BuildingBlocks.Messaging/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Security/BuildingBlocks.Security.csproj", "src/BuildingBlocks/BuildingBlocks.Security/"]
RUN dotnet restore "src/Services/Payment/Payment.Api/Payment.Api.csproj"

COPY . .
WORKDIR "/src/src/Services/Payment/Payment.Api"
RUN dotnet publish "Payment.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Payment.Api.dll"]
