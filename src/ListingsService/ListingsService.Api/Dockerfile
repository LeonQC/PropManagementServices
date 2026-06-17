FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# Build context is the repo's src/ directory so the shared library (Shared/…)
# is available alongside the service projects.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["ListingsService/ListingsService.Api/ListingsService.Api.csproj", "ListingsService/ListingsService.Api/"]
COPY ["ListingsService/ListingsService.Business/ListingsService.Business.csproj", "ListingsService/ListingsService.Business/"]
COPY ["ListingsService/ListingsService.DataAccess/ListingsService.DataAccess.csproj", "ListingsService/ListingsService.DataAccess/"]
COPY ["ListingsService/ListingsService.Models/ListingsService.Models.csproj", "ListingsService/ListingsService.Models/"]
COPY ["Shared/PropTrack.Messaging/PropTrack.Messaging.csproj", "Shared/PropTrack.Messaging/"]
RUN dotnet restore "ListingsService/ListingsService.Api/ListingsService.Api.csproj"
COPY . .
RUN dotnet publish "ListingsService/ListingsService.Api/ListingsService.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ListingsService.Api.dll"]
