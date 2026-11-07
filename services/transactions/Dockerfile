# Build a partir da RAIZ do monorepo (context: repo root).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props NuGet.config ./
COPY services/transactions/ services/transactions/

RUN dotnet restore services/transactions/src/Expense.Tracking.Transactions.Api/Expense.Tracking.Transactions.Api.csproj
RUN dotnet publish services/transactions/src/Expense.Tracking.Transactions.Api/Expense.Tracking.Transactions.Api.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Expense.Tracking.Transactions.Api.dll"]
