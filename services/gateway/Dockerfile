# Build a partir da RAIZ do monorepo (context: repo root).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props NuGet.config ./
COPY services/gateway/ services/gateway/

RUN dotnet restore services/gateway/src/Expense.Tracking.Gateway/Expense.Tracking.Gateway.csproj
RUN dotnet publish services/gateway/src/Expense.Tracking.Gateway/Expense.Tracking.Gateway.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Expense.Tracking.Gateway.dll"]
