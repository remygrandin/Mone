FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 162/udp
EXPOSE 514/udp
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Mone/Directory.Build.props Mone/
COPY Mone-Plugins/Directory.Build.props Mone-Plugins/
COPY Mone/src/Mone.ProbeExecutor/Mone.ProbeExecutor.csproj Mone/src/Mone.ProbeExecutor/
COPY Mone/src/Mone.Contracts/Mone.Contracts.csproj Mone/src/Mone.Contracts/
COPY Mone/src/Mone.Infrastructure/Mone.Infrastructure.csproj Mone/src/Mone.Infrastructure/
COPY Mone/src/Mone.Messaging/Mone.Messaging.csproj Mone/src/Mone.Messaging/
COPY Mone/src/Mone.PluginEngine/Mone.PluginEngine.csproj Mone/src/Mone.PluginEngine/
COPY Mone-Plugins/probes/Mone.Plugins.Probes.Ping/Mone.Plugins.Probes.Ping.csproj Mone-Plugins/probes/Mone.Plugins.Probes.Ping/
COPY Mone-Plugins/probes/Mone.Plugins.Probes.Https/Mone.Plugins.Probes.Https.csproj Mone-Plugins/probes/Mone.Plugins.Probes.Https/
COPY Mone-Plugins/probes/Mone.Plugins.Probes.Webhook/Mone.Plugins.Probes.Webhook.csproj Mone-Plugins/probes/Mone.Plugins.Probes.Webhook/
COPY Mone-Plugins/probes/Mone.Plugins.Probes.SnmpTrap/Mone.Plugins.Probes.SnmpTrap.csproj Mone-Plugins/probes/Mone.Plugins.Probes.SnmpTrap/
COPY Mone-Plugins/probes/Mone.Plugins.Probes.Syslog/Mone.Plugins.Probes.Syslog.csproj Mone-Plugins/probes/Mone.Plugins.Probes.Syslog/
RUN dotnet restore Mone/src/Mone.ProbeExecutor/Mone.ProbeExecutor.csproj -p:NuGetAudit=false

COPY Mone/src/ Mone/src/
COPY Mone-Plugins/ Mone-Plugins/
RUN dotnet publish Mone/src/Mone.ProbeExecutor/Mone.ProbeExecutor.csproj -c Release -o /app/publish -p:NuGetAudit=false
RUN dotnet publish Mone-Plugins/probes/Mone.Plugins.Probes.Ping/Mone.Plugins.Probes.Ping.csproj -c Release -o /app/plugins/Mone.Plugins.Probes.Ping -p:NuGetAudit=false
RUN dotnet publish Mone-Plugins/probes/Mone.Plugins.Probes.Https/Mone.Plugins.Probes.Https.csproj -c Release -o /app/plugins/Mone.Plugins.Probes.Https -p:NuGetAudit=false
RUN dotnet publish Mone-Plugins/probes/Mone.Plugins.Probes.Webhook/Mone.Plugins.Probes.Webhook.csproj -c Release -o /app/plugins/Mone.Plugins.Probes.Webhook -p:NuGetAudit=false
RUN dotnet publish Mone-Plugins/probes/Mone.Plugins.Probes.SnmpTrap/Mone.Plugins.Probes.SnmpTrap.csproj -c Release -o /app/plugins/Mone.Plugins.Probes.SnmpTrap -p:NuGetAudit=false
RUN dotnet publish Mone-Plugins/probes/Mone.Plugins.Probes.Syslog/Mone.Plugins.Probes.Syslog.csproj -c Release -o /app/plugins/Mone.Plugins.Probes.Syslog -p:NuGetAudit=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /app/plugins/ ./plugins/
ENTRYPOINT ["dotnet", "Mone.ProbeExecutor.dll"]
