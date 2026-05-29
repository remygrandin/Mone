FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 162/udp
EXPOSE 514/udp
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Mone/Directory.Build.props Mone/
COPY Mone/src/Mone.ProbeExecutor/Mone.ProbeExecutor.csproj Mone/src/Mone.ProbeExecutor/
COPY Mone/src/Mone.Contracts/Mone.Contracts.csproj Mone/src/Mone.Contracts/
COPY Mone/src/Mone.Infrastructure/Mone.Infrastructure.csproj Mone/src/Mone.Infrastructure/
COPY Mone/src/Mone.Messaging/Mone.Messaging.csproj Mone/src/Mone.Messaging/
COPY Mone/src/Mone.PluginEngine/Mone.PluginEngine.csproj Mone/src/Mone.PluginEngine/
RUN dotnet restore Mone/src/Mone.ProbeExecutor/Mone.ProbeExecutor.csproj -p:NuGetAudit=false

COPY Mone/src/ Mone/src/
RUN dotnet publish Mone/src/Mone.ProbeExecutor/Mone.ProbeExecutor.csproj -c Release -o /app/publish -p:NuGetAudit=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /app/plugins
ENTRYPOINT ["dotnet", "Mone.ProbeExecutor.dll"]
