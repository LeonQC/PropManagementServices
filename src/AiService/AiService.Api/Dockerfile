FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# Build context is the repo's src/ directory, matching the other services. This
# one references no shared library — Phase 1 has no Kafka half, so PropTrack.Messaging
# is not restored here.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["AiService/AiService.Api/AiService.Api.csproj", "AiService/AiService.Api/"]
COPY ["AiService/AiService.Business/AiService.Business.csproj", "AiService/AiService.Business/"]
COPY ["AiService/AiService.DataAccess/AiService.DataAccess.csproj", "AiService/AiService.DataAccess/"]
COPY ["AiService/AiService.Models/AiService.Models.csproj", "AiService/AiService.Models/"]
RUN dotnet restore "AiService/AiService.Api/AiService.Api.csproj"
COPY . .
RUN dotnet publish "AiService/AiService.Api/AiService.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AiService.Api.dll"]
