FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# Build context is the repo's src/ directory so the shared library (Shared/…) is
# available alongside the service projects.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["AiService/AiService.Api/AiService.Api.csproj", "AiService/AiService.Api/"]
COPY ["AiService/AiService.Business/AiService.Business.csproj", "AiService/AiService.Business/"]
COPY ["AiService/AiService.DataAccess/AiService.DataAccess.csproj", "AiService/AiService.DataAccess/"]
COPY ["AiService/AiService.Models/AiService.Models.csproj", "AiService/AiService.Models/"]
COPY ["Shared/PropTrack.Messaging/PropTrack.Messaging.csproj", "Shared/PropTrack.Messaging/"]
RUN dotnet restore "AiService/AiService.Api/AiService.Api.csproj"
COPY . .
RUN dotnet publish "AiService/AiService.Api/AiService.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AiService.Api.dll"]
