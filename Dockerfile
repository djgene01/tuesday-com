# Build once, run as a non-root user on the ASP.NET runtime image.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Tuesday.slnx ./
COPY src/Tuesday.Contracts/*.csproj src/Tuesday.Contracts/
COPY src/Tuesday.Domain/*.csproj src/Tuesday.Domain/
COPY src/Tuesday.Application/*.csproj src/Tuesday.Application/
COPY src/Tuesday.Infrastructure/*.csproj src/Tuesday.Infrastructure/
COPY src/Tuesday.Web/*.csproj src/Tuesday.Web/
COPY src/Tuesday.Worker/*.csproj src/Tuesday.Worker/

# copy the props/targets that the Web SDK needs before restore.
COPY src/Tuesday.Web/Components/ src/Tuesday.Web/Components/
COPY src/Tuesday.Web/wwwroot/ src/Tuesday.Web/wwwroot/

# Restore must import the Web SDK's static-asset machinery, or the Blazor framework files
# (microsoft.aspnetcore.app.internal.assets -> wwwroot/_framework) are silently absent from
# the publish output and the container serves an app whose JS 404s. Restoring only the csproj
# is not enough; restore the publish target itself.
RUN dotnet restore src/Tuesday.Web/Tuesday.Web.csproj -p:RestoreStaticWebAssets=true

COPY src/ src/
RUN dotnet publish src/Tuesday.Web/Tuesday.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    Storage__Root=/var/tuesday/files

RUN mkdir -p /var/tuesday/files && chown -R $APP_UID:$APP_UID /var/tuesday

COPY --from=build /app/publish .
# The .NET 8+ runtime images ship a non-root "app" user with APP_UID set; reuse it rather
# than creating one (the Debian 13 base has no adduser binary).
USER $APP_UID

EXPOSE 8080

ENTRYPOINT ["dotnet", "Tuesday.Web.dll"]
