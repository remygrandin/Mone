FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Mone/Directory.Build.props Mone/
COPY Mone/src/Mone.Api/Mone.Api.csproj Mone/src/Mone.Api/
COPY Mone/src/Mone.Contracts/Mone.Contracts.csproj Mone/src/Mone.Contracts/
COPY Mone/src/Mone.Infrastructure/Mone.Infrastructure.csproj Mone/src/Mone.Infrastructure/
COPY Mone/src/Mone.Messaging/Mone.Messaging.csproj Mone/src/Mone.Messaging/
COPY Mone/src/Mone.PluginEngine/Mone.PluginEngine.csproj Mone/src/Mone.PluginEngine/
COPY Mone/src/Mone.Dashboard/Mone.Dashboard.csproj Mone/src/Mone.Dashboard/
RUN dotnet restore Mone/src/Mone.Api/Mone.Api.csproj -p:NuGetAudit=false

COPY Mone/src/ Mone/src/
RUN dotnet publish Mone/src/Mone.Api/Mone.Api.csproj -c Release -o /app/publish -p:NuGetAudit=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /app/plugins
ENTRYPOINT ["dotnet", "Mone.Api.dll"]
