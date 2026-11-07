# Build a partir da RAIZ do monorepo (context: repo root) para enxergar
# Directory.Build.props / Directory.Packages.props / NuGet.config.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Arquivos de build compartilhados (central package management + feeds).
COPY Directory.Build.props Directory.Packages.props NuGet.config ./

# Codigo do servico Identity.
COPY services/identity/ services/identity/

RUN dotnet restore services/identity/src/Expense.Tracking.Identity.Api/Expense.Tracking.Identity.Api.csproj
RUN dotnet publish services/identity/src/Expense.Tracking.Identity.Api/Expense.Tracking.Identity.Api.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Expense.Tracking.Identity.Api.dll"]
