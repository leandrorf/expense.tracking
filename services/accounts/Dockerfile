# Build a partir da RAIZ do monorepo (context: repo root).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props NuGet.config ./
COPY services/accounts/ services/accounts/

RUN dotnet restore services/accounts/src/Expense.Tracking.Accounts.Api/Expense.Tracking.Accounts.Api.csproj
RUN dotnet publish services/accounts/src/Expense.Tracking.Accounts.Api/Expense.Tracking.Accounts.Api.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Expense.Tracking.Accounts.Api.dll"]
