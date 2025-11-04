# ======================
# BUILD STAGE
# ======================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files separately to cache dependencies
COPY ./src/Teammy.Api/Teammy.Api.csproj ./Teammy.Api/
RUN dotnet restore ./Teammy.Api/Teammy.Api.csproj

# Copy all remaining source
COPY ./src ./ 

RUN dotnet publish ./Teammy.Api/Teammy.Api.csproj -c Release -o /app/out /p:UseAppHost=false

# ======================
# RUNTIME STAGE
# ======================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Copy compiled output from build stage
COPY --from=build /app/out ./

# Run application
ENTRYPOINT ["dotnet", "Teammy.Api.dll"]
