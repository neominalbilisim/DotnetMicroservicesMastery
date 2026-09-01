FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/BackgroundJobs/JobService/JobService.csproj", "src/BackgroundJobs/JobService/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Common/BuildingBlocks.Common.csproj", "src/BuildingBlocks/BuildingBlocks.Common/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Security/BuildingBlocks.Security.csproj", "src/BuildingBlocks/BuildingBlocks.Security/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Observability/BuildingBlocks.Observability.csproj", "src/BuildingBlocks/BuildingBlocks.Observability/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Resilience/BuildingBlocks.Resilience.csproj", "src/BuildingBlocks/BuildingBlocks.Resilience/"]
RUN dotnet restore "src/BackgroundJobs/JobService/JobService.csproj"

COPY . .
WORKDIR "/src/src/BackgroundJobs/JobService"
RUN dotnet publish "JobService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "JobService.dll"]
