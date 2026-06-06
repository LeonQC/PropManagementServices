FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["ListingsService.Api/ListingsService.Api.csproj", "ListingsService.Api/"]
COPY ["ListingsService.Application/ListingsService.Application.csproj", "ListingsService.Application/"]
COPY ["ListingsService.Domain/ListingsService.Domain.csproj", "ListingsService.Domain/"]
COPY ["ListingsService.Infrastructure/ListingsService.Infrastructure.csproj", "ListingsService.Infrastructure/"]
RUN dotnet restore "ListingsService.Api/ListingsService.Api.csproj"
COPY . .
RUN dotnet publish "ListingsService.Api/ListingsService.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ListingsService.Api.dll"]
