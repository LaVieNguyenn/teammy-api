# ======================
# BUILD STAGE
# ======================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy only csproj files first (to leverage Docker layer caching)
COPY ./Teammy.Api/Teammy.Api.csproj ./Teammy.Api/
COPY ./Teammy.Domain/Teammy.Domain.csproj ./Teammy.Domain/
COPY ./Teammy.Application/Teammy.Application.csproj ./Teammy.Application/
COPY ./Teammy.Infrastructure/Teammy.Infrastructure.csproj ./Teammy.Infrastructure/

# Restore dependencies
RUN dotnet restore ./Teammy.Api/Teammy.Api.csproj

# Copy all remaining source code
COPY . .

# Build and publish release output
RUN dotnet publish ./Teammy.Api/Teammy.Api.csproj -c Release -o /out /p:UseAppHost=false


# ======================
# RUNTIME STAGE
# ======================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Copy published output from build stage
COPY --from=build /out ./

# Run application
ENTRYPOINT ["dotnet", "Teammy.Api.dll"]
