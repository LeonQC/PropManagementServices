FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# Build context is the repo's src/ directory so the shared library (Shared/…)
# is available alongside the service projects.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["DocumentsService/DocumentsService.Api/DocumentsService.Api.csproj", "DocumentsService/DocumentsService.Api/"]
COPY ["DocumentsService/DocumentsService.Business/DocumentsService.Business.csproj", "DocumentsService/DocumentsService.Business/"]
COPY ["DocumentsService/DocumentsService.DataAccess/DocumentsService.DataAccess.csproj", "DocumentsService/DocumentsService.DataAccess/"]
COPY ["DocumentsService/DocumentsService.Models/DocumentsService.Models.csproj", "DocumentsService/DocumentsService.Models/"]
COPY ["Shared/PropTrack.Messaging/PropTrack.Messaging.csproj", "Shared/PropTrack.Messaging/"]
RUN dotnet restore "DocumentsService/DocumentsService.Api/DocumentsService.Api.csproj"
COPY . .
RUN dotnet publish "DocumentsService/DocumentsService.Api/DocumentsService.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DocumentsService.Api.dll"]
