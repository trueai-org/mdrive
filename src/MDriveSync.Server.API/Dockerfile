# Base image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base

# Set the timezone and locale
ENV TZ=Asia/Shanghai
RUN ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && echo $TZ > /etc/timezone

# Switch to non-root user
USER app

WORKDIR /app
EXPOSE 8080

# Build image
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/MDriveSync.Server.API/MDriveSync.Server.API.csproj", "src/MDriveSync.Server.API/"]
COPY ["src/MDriveSync.Core/MDriveSync.Core.csproj", "src/MDriveSync.Core/"]
RUN dotnet restore "./src/MDriveSync.Server.API/./MDriveSync.Server.API.csproj"
COPY . .
WORKDIR "/src/src/MDriveSync.Server.API"
RUN dotnet build "./MDriveSync.Server.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./MDriveSync.Server.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MDriveSync.Server.API.dll"]