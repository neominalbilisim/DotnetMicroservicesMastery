FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Gateway/ApiGateway/ApiGateway.csproj", "src/Gateway/ApiGateway/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Common/BuildingBlocks.Common.csproj", "src/BuildingBlocks/BuildingBlocks.Common/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Security/BuildingBlocks.Security.csproj", "src/BuildingBlocks/BuildingBlocks.Security/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Observability/BuildingBlocks.Observability.csproj", "src/BuildingBlocks/BuildingBlocks.Observability/"]
RUN dotnet restore "src/Gateway/ApiGateway/ApiGateway.csproj"

COPY . .
WORKDIR "/src/src/Gateway/ApiGateway"
RUN dotnet publish "ApiGateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ApiGateway.dll"]
