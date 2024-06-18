#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER app
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/MDriveSync.Client.API/MDriveSync.Client.API.csproj", "src/MDriveSync.Client.API/"]
COPY ["src/MDriveSync.Core/MDriveSync.Core.csproj", "src/MDriveSync.Core/"]
RUN dotnet restore "./src/MDriveSync.Client.API/./MDriveSync.Client.API.csproj"
COPY . .
WORKDIR "/src/src/MDriveSync.Client.API"
RUN dotnet build "./MDriveSync.Client.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./MDriveSync.Client.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
USER root  # 确保以 root 用户运行
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MDriveSync.Client.API.dll"]