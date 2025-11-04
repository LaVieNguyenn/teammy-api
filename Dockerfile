# ---- Build Stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ./ ./
RUN dotnet restore src/Teammy.Api/Teammy.Api.csproj
RUN dotnet publish src/Teammy.Api/Teammy.Api.csproj -c Release -o /app/out /p:UseAppHost=false

# ---- Runtime Stage ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/out ./

ENTRYPOINT ["dotnet", "Teammy.Api.dll"]
