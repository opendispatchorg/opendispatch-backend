# The deployable artifact. `dotnet run` and `make migrate` need the SDK, the tool manifest and this
# working tree; a shop running OpenDispatch has none of those, so this is what they run instead.
#
# One image serves all three things the host does — `migrate`, `create-user`, and serving traffic —
# because they are one application with three entry arguments. A separate migration image would be
# a second thing to build, tag and keep in step with the schema it applies.

# ---- build ------------------------------------------------------------------------------------
# The exact SDK global.json pins, not the floating 10.0 tag: `rollForward: latestPatch` stays
# inside one feature band, so the floating tag stops satisfying it the day .NET publishes the next
# one (it already does — 10.0.400 against a 10.0.302 pin). Bumping global.json means bumping this
# line, which is the trade for an image built by the same SDK as CI and every developer.
FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /src

# Restore against the project graph alone, so a change to a .cs file does not invalidate the
# restore layer. Directory.Build.props comes first because every project imports it.
COPY global.json Directory.Build.props ./
COPY src/Domain/Domain.csproj src/Domain/
COPY src/Scheduling/Scheduling.csproj src/Scheduling/
COPY src/Application/Application.csproj src/Application/
COPY src/Contracts/Contracts.csproj src/Contracts/
COPY src/Infrastructure/Infrastructure.csproj src/Infrastructure/
COPY src/Api/Api.csproj src/Api/
RUN dotnet restore src/Api/Api.csproj

COPY src/ src/
# No --no-restore: the copy above brings in sources the restore layer has not seen. Release,
# because this is what runs in production, and warnings are errors here exactly as they are in CI.
RUN dotnet publish src/Api/Api.csproj -c Release -o /app --no-restore

# ---- runtime ----------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The framework image ships a non-root user; using it means a container breakout is not root on the
# host, and it costs nothing here because nothing this application writes lives inside the image.
USER $APP_UID

# Kestrel binds this port inside the container. Nothing terminates TLS here: every deployment shape
# Document 1 describes puts a proxy in front, which is also what ReverseProxy:Enabled is for.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Attachment content — photographs and signatures — is the one thing the application writes to a
# filesystem. It must be a volume, or a restart loses the field's evidence. See AttachmentOptions.
ENV Attachments__Root=/var/lib/opendispatch/attachments
VOLUME ["/var/lib/opendispatch/attachments"]

COPY --from=build /app .

# No arguments: the container serves. `migrate` and `create-user …` are the same image with a
# different command, which is how a deployment applies the schema and writes its first login.
ENTRYPOINT ["dotnet", "OpenDispatch.Api.dll"]
