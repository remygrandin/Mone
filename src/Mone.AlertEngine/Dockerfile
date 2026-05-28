FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Mone/Directory.Build.props Mone/
COPY Mone-Plugins/Directory.Build.props Mone-Plugins/
COPY Mone/src/Mone.AlertEngine/Mone.AlertEngine.csproj Mone/src/Mone.AlertEngine/
COPY Mone/src/Mone.Contracts/Mone.Contracts.csproj Mone/src/Mone.Contracts/
COPY Mone/src/Mone.Infrastructure/Mone.Infrastructure.csproj Mone/src/Mone.Infrastructure/
COPY Mone/src/Mone.Messaging/Mone.Messaging.csproj Mone/src/Mone.Messaging/
COPY Mone/src/Mone.PluginEngine/Mone.PluginEngine.csproj Mone/src/Mone.PluginEngine/
COPY Mone-Plugins/notifications/Mone.Plugins.Notifications.Email/Mone.Plugins.Notifications.Email.csproj Mone-Plugins/notifications/Mone.Plugins.Notifications.Email/
COPY Mone-Plugins/notifications/Mone.Plugins.Notifications.Slack/Mone.Plugins.Notifications.Slack.csproj Mone-Plugins/notifications/Mone.Plugins.Notifications.Slack/
COPY Mone-Plugins/notifications/Mone.Plugins.Notifications.Teams/Mone.Plugins.Notifications.Teams.csproj Mone-Plugins/notifications/Mone.Plugins.Notifications.Teams/
COPY Mone-Plugins/notifications/Mone.Plugins.Notifications.Webhook/Mone.Plugins.Notifications.Webhook.csproj Mone-Plugins/notifications/Mone.Plugins.Notifications.Webhook/
RUN dotnet restore Mone/src/Mone.AlertEngine/Mone.AlertEngine.csproj -p:NuGetAudit=false

COPY Mone/src/ Mone/src/
COPY Mone-Plugins/ Mone-Plugins/
RUN dotnet publish Mone/src/Mone.AlertEngine/Mone.AlertEngine.csproj -c Release -o /app/publish -p:NuGetAudit=false
RUN dotnet publish Mone-Plugins/notifications/Mone.Plugins.Notifications.Email/Mone.Plugins.Notifications.Email.csproj -c Release -o /app/plugins/Mone.Plugins.Notifications.Email -p:NuGetAudit=false
RUN dotnet publish Mone-Plugins/notifications/Mone.Plugins.Notifications.Slack/Mone.Plugins.Notifications.Slack.csproj -c Release -o /app/plugins/Mone.Plugins.Notifications.Slack -p:NuGetAudit=false
RUN dotnet publish Mone-Plugins/notifications/Mone.Plugins.Notifications.Teams/Mone.Plugins.Notifications.Teams.csproj -c Release -o /app/plugins/Mone.Plugins.Notifications.Teams -p:NuGetAudit=false
RUN dotnet publish Mone-Plugins/notifications/Mone.Plugins.Notifications.Webhook/Mone.Plugins.Notifications.Webhook.csproj -c Release -o /app/plugins/Mone.Plugins.Notifications.Webhook -p:NuGetAudit=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /app/plugins/ ./plugins/
ENTRYPOINT ["dotnet", "Mone.AlertEngine.dll"]
