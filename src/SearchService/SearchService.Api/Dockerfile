FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# Build context is the repo's src/ directory so the shared library (Shared/…)
# is available alongside the service projects.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["SearchService/SearchService.Api/SearchService.Api.csproj", "SearchService/SearchService.Api/"]
COPY ["SearchService/SearchService.Business/SearchService.Business.csproj", "SearchService/SearchService.Business/"]
COPY ["SearchService/SearchService.DataAccess/SearchService.DataAccess.csproj", "SearchService/SearchService.DataAccess/"]
COPY ["SearchService/SearchService.Models/SearchService.Models.csproj", "SearchService/SearchService.Models/"]
COPY ["Shared/PropTrack.Messaging/PropTrack.Messaging.csproj", "Shared/PropTrack.Messaging/"]
RUN dotnet restore "SearchService/SearchService.Api/SearchService.Api.csproj"
COPY . .
RUN dotnet publish "SearchService/SearchService.Api/SearchService.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SearchService.Api.dll"]
