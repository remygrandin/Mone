FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Mone/Directory.Build.props Mone/
COPY Mone-Plugins/Directory.Build.props Mone-Plugins/
COPY Mone/src/Mone.Api/Mone.Api.csproj Mone/src/Mone.Api/
COPY Mone/src/Mone.Contracts/Mone.Contracts.csproj Mone/src/Mone.Contracts/
COPY Mone/src/Mone.Infrastructure/Mone.Infrastructure.csproj Mone/src/Mone.Infrastructure/
COPY Mone/src/Mone.Messaging/Mone.Messaging.csproj Mone/src/Mone.Messaging/
COPY Mone/src/Mone.PluginEngine/Mone.PluginEngine.csproj Mone/src/Mone.PluginEngine/
COPY Mone/src/Mone.Dashboard/Mone.Dashboard.csproj Mone/src/Mone.Dashboard/
COPY Mone-Plugins/probes/Mone.Plugins.Probes.Ping/Mone.Plugins.Probes.Ping.csproj Mone-Plugins/probes/Mone.Plugins.Probes.Ping/
COPY Mone-Plugins/probes/Mone.Plugins.Probes.Https/Mone.Plugins.Probes.Https.csproj Mone-Plugins/probes/Mone.Plugins.Probes.Https/
COPY Mone-Plugins/probes/Mone.Plugins.Probes.Webhook/Mone.Plugins.Probes.Webhook.csproj Mone-Plugins/probes/Mone.Plugins.Probes.Webhook/
COPY Mone-Plugins/probes/Mone.Plugins.Probes.SnmpTrap/Mone.Plugins.Probes.SnmpTrap.csproj Mone-Plugins/probes/Mone.Plugins.Probes.SnmpTrap/
COPY Mone-Plugins/probes/Mone.Plugins.Probes.Syslog/Mone.Plugins.Probes.Syslog.csproj Mone-Plugins/probes/Mone.Plugins.Probes.Syslog/
COPY Mone-Plugins/checkers/Mone.Plugins.Checkers.ThresholdChecker/Mone.Plugins.Checkers.ThresholdChecker.csproj Mone-Plugins/checkers/Mone.Plugins.Checkers.ThresholdChecker/
COPY Mone-Plugins/checkers/Mone.Plugins.Checkers.ValueChecker/Mone.Plugins.Checkers.ValueChecker.csproj Mone-Plugins/checkers/Mone.Plugins.Checkers.ValueChecker/
COPY Mone-Plugins/notifications/Mone.Plugins.Notifications.Email/Mone.Plugins.Notifications.Email.csproj Mone-Plugins/notifications/Mone.Plugins.Notifications.Email/
RUN dotnet restore Mone/src/Mone.Api/Mone.Api.csproj -p:NuGetAudit=false

COPY Mone/src/ Mone/src/
COPY Mone-Plugins/ Mone-Plugins/
RUN dotnet publish Mone/src/Mone.Api/Mone.Api.csproj -c Release -o /app/publish -p:NuGetAudit=false
RUN dotnet publish Mone-Plugins/probes/Mone.Plugins.Probes.Ping/Mone.Plugins.Probes.Ping.csproj -c Release -o /app/plugins/Mone.Plugins.Probes.Ping -p:NuGetAudit=false
RUN dotnet publish Mone-Plugins/probes/Mone.Plugins.Probes.Https/Mone.Plugins.Probes.Https.csproj -c Release -o /app/plugins/Mone.Plugins.Probes.Https -p:NuGetAudit=false
RUN dotnet publish Mone-Plugins/probes/Mone.Plugins.Probes.Webhook/Mone.Plugins.Probes.Webhook.csproj -c Release -o /app/plugins/Mone.Plugins.Probes.Webhook -p:NuGetAudit=false
RUN dotnet publish Mone-Plugins/probes/Mone.Plugins.Probes.SnmpTrap/Mone.Plugins.Probes.SnmpTrap.csproj -c Release -o /app/plugins/Mone.Plugins.Probes.SnmpTrap -p:NuGetAudit=false
RUN dotnet publish Mone-Plugins/probes/Mone.Plugins.Probes.Syslog/Mone.Plugins.Probes.Syslog.csproj -c Release -o /app/plugins/Mone.Plugins.Probes.Syslog -p:NuGetAudit=false
RUN dotnet publish Mone-Plugins/checkers/Mone.Plugins.Checkers.ThresholdChecker/Mone.Plugins.Checkers.ThresholdChecker.csproj -c Release -o /app/plugins/Mone.Plugins.Checkers.ThresholdChecker -p:NuGetAudit=false
RUN dotnet publish Mone-Plugins/checkers/Mone.Plugins.Checkers.ValueChecker/Mone.Plugins.Checkers.ValueChecker.csproj -c Release -o /app/plugins/Mone.Plugins.Checkers.ValueChecker -p:NuGetAudit=false
RUN dotnet publish Mone-Plugins/notifications/Mone.Plugins.Notifications.Email/Mone.Plugins.Notifications.Email.csproj -c Release -o /app/plugins/Mone.Plugins.Notifications.Email -p:NuGetAudit=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /app/plugins/ ./plugins/
ENTRYPOINT ["dotnet", "Mone.Api.dll"]
