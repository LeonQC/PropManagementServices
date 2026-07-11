FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# Build context is the repo's src/ directory so the shared library (Shared/…)
# is available alongside the service projects.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["DealsService/DealsService.Api/DealsService.Api.csproj", "DealsService/DealsService.Api/"]
COPY ["DealsService/DealsService.Business/DealsService.Business.csproj", "DealsService/DealsService.Business/"]
COPY ["DealsService/DealsService.DataAccess/DealsService.DataAccess.csproj", "DealsService/DealsService.DataAccess/"]
COPY ["DealsService/DealsService.Models/DealsService.Models.csproj", "DealsService/DealsService.Models/"]
COPY ["Shared/PropTrack.Messaging/PropTrack.Messaging.csproj", "Shared/PropTrack.Messaging/"]
RUN dotnet restore "DealsService/DealsService.Api/DealsService.Api.csproj"
COPY . .
RUN dotnet publish "DealsService/DealsService.Api/DealsService.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DealsService.Api.dll"]
