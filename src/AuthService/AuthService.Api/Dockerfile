FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# Build context is the repo's src/ directory (matches listings-service), so all
# AuthService projects are available under AuthService/.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["AuthService/AuthService.Api/AuthService.Api.csproj", "AuthService/AuthService.Api/"]
COPY ["AuthService/AuthService.Business/AuthService.Business.csproj", "AuthService/AuthService.Business/"]
COPY ["AuthService/AuthService.DataAccess/AuthService.DataAccess.csproj", "AuthService/AuthService.DataAccess/"]
COPY ["AuthService/AuthService.Models/AuthService.Models.csproj", "AuthService/AuthService.Models/"]
RUN dotnet restore "AuthService/AuthService.Api/AuthService.Api.csproj"
COPY . .
RUN dotnet publish "AuthService/AuthService.Api/AuthService.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AuthService.Api.dll"]
