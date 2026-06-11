FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["ListingsService.Api/ListingsService.Api.csproj", "ListingsService.Api/"]
COPY ["ListingsService.Business/ListingsService.Business.csproj", "ListingsService.Business/"]
COPY ["ListingsService.DataAccess/ListingsService.DataAccess.csproj", "ListingsService.DataAccess/"]
COPY ["ListingsService.Models/ListingsService.Models.csproj", "ListingsService.Models/"]
RUN dotnet restore "ListingsService.Api/ListingsService.Api.csproj"
COPY . .
RUN dotnet publish "ListingsService.Api/ListingsService.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ListingsService.Api.dll"]
