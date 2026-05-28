FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Mone/Directory.Build.props Mone/
COPY Mone-Plugins/Directory.Build.props Mone-Plugins/
COPY Mone/src/Mone.CheckerEngine/Mone.CheckerEngine.csproj Mone/src/Mone.CheckerEngine/
COPY Mone/src/Mone.Contracts/Mone.Contracts.csproj Mone/src/Mone.Contracts/
COPY Mone/src/Mone.Infrastructure/Mone.Infrastructure.csproj Mone/src/Mone.Infrastructure/
COPY Mone/src/Mone.Messaging/Mone.Messaging.csproj Mone/src/Mone.Messaging/
COPY Mone/src/Mone.PluginEngine/Mone.PluginEngine.csproj Mone/src/Mone.PluginEngine/
COPY Mone-Plugins/checkers/Mone.Plugins.Checkers.ThresholdChecker/Mone.Plugins.Checkers.ThresholdChecker.csproj Mone-Plugins/checkers/Mone.Plugins.Checkers.ThresholdChecker/
COPY Mone-Plugins/checkers/Mone.Plugins.Checkers.ValueChecker/Mone.Plugins.Checkers.ValueChecker.csproj Mone-Plugins/checkers/Mone.Plugins.Checkers.ValueChecker/
RUN dotnet restore Mone/src/Mone.CheckerEngine/Mone.CheckerEngine.csproj -p:NuGetAudit=false

COPY Mone/src/ Mone/src/
COPY Mone-Plugins/ Mone-Plugins/
RUN dotnet publish Mone/src/Mone.CheckerEngine/Mone.CheckerEngine.csproj -c Release -o /app/publish -p:NuGetAudit=false
RUN dotnet publish Mone-Plugins/checkers/Mone.Plugins.Checkers.ThresholdChecker/Mone.Plugins.Checkers.ThresholdChecker.csproj -c Release -o /app/plugins/Mone.Plugins.Checkers.ThresholdChecker -p:NuGetAudit=false
RUN dotnet publish Mone-Plugins/checkers/Mone.Plugins.Checkers.ValueChecker/Mone.Plugins.Checkers.ValueChecker.csproj -c Release -o /app/plugins/Mone.Plugins.Checkers.ValueChecker -p:NuGetAudit=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /app/plugins/ ./plugins/
ENTRYPOINT ["dotnet", "Mone.CheckerEngine.dll"]
