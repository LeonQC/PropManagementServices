FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["{{Svc}}Service.Api/{{Svc}}Service.Api.csproj", "{{Svc}}Service.Api/"]
COPY ["{{Svc}}Service.Business/{{Svc}}Service.Business.csproj", "{{Svc}}Service.Business/"]
COPY ["{{Svc}}Service.DataAccess/{{Svc}}Service.DataAccess.csproj", "{{Svc}}Service.DataAccess/"]
COPY ["{{Svc}}Service.Models/{{Svc}}Service.Models.csproj", "{{Svc}}Service.Models/"]
RUN dotnet restore "{{Svc}}Service.Api/{{Svc}}Service.Api.csproj"
COPY . .
RUN dotnet publish "{{Svc}}Service.Api/{{Svc}}Service.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "{{Svc}}Service.Api.dll"]
