#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0 AS base
WORKDIR /home/site/wwwroot
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
RUN mkdir -p .artifacts
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["nuget.config", "."]
COPY ["src/Platform/Storyteller/Api.Functions/src/Api.Functions.csproj", "src/Platform/Storyteller/Api.Functions/src/"]
COPY ["src/Platform/Storyteller/Abstraction/src/Abstraction.csproj", "src/Platform/Storyteller/Abstraction/src/"]
COPY ["src/Platform/Storyteller/AzureAd/src/AzureAd.csproj", "src/Platform/Storyteller/AzureAd/src/"]
COPY ["src/Platform/Storyteller/CosmosDb/src/CosmosDb.csproj", "src/Platform/Storyteller/CosmosDb/src/"]
COPY ["src/Libraries/Utils/Async/src/Async.csproj", "src/Libraries/Utils/Async/src/"]
RUN dotnet restore "./src/Platform/Storyteller/Api.Functions/src/./Api.Functions.csproj"
COPY . .
WORKDIR "/src/src/Platform/Storyteller/Api.Functions/src"
RUN dotnet build "./Api.Functions.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Api.Functions.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /home/site/wwwroot
COPY --from=publish /app/publish .
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true
