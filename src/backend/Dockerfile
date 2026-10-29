# Build context: src/backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, so the layer is cached while only source files change.
COPY Sonrisa.slnx ./
COPY Sonrisa.Api/Sonrisa.Api.csproj Sonrisa.Api/
COPY Sonrisa.Application/Sonrisa.Application.csproj Sonrisa.Application/
COPY Sonrisa.Contracts/Sonrisa.Contracts.csproj Sonrisa.Contracts/
COPY Sonrisa.Domain/Sonrisa.Domain.csproj Sonrisa.Domain/
COPY Sonrisa.Infrastructure/Sonrisa.Infrastructure.csproj Sonrisa.Infrastructure/
RUN dotnet restore Sonrisa.Api/Sonrisa.Api.csproj

COPY . .
RUN dotnet publish Sonrisa.Api/Sonrisa.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Sonrisa.Api.dll"]
