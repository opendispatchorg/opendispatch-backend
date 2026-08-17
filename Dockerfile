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
#
# `.editorconfig` is here because leaving it out made this build a *different* build from the one
# CI and every developer runs: analyzer severities and the `generated_code = true` that exempts EF's
# migrations both live in it, so without it the image applied rules to generated files that nothing
# else did, and failed on code no local build objects to. A build that can only be reproduced in
# Docker is one nobody debugs.
COPY global.json Directory.Build.props .editorconfig ./
COPY src/Domain/Domain.csproj src/Domain/
COPY src/Scheduling/Scheduling.csproj src/Scheduling/
COPY src/Application/Application.csproj src/Application/
COPY src/Contracts/Contracts.csproj src/Contracts/
COPY src/Infrastructure/Infrastructure.csproj src/Infrastructure/
COPY src/Api/Api.csproj src/Api/
RUN dotnet restore src/Api/Api.csproj

COPY src/ src/

# The commit this image was built from. Defaulted rather than required, so `docker build .` on a
# working tree still works and honestly reports `local` — CI passes the real SHA. It reaches the
# running container through AssemblyInformationalVersion, which BuildVersion reads and the host logs
# at startup: a container that cannot name its own commit makes every incident longer.
ARG SOURCE_REVISION_ID=local

# No --no-restore: the copy above brings in sources the restore layer has not seen. Release,
# because this is what runs in production, and warnings are errors here exactly as they are in CI.
RUN dotnet publish src/Api/Api.csproj -c Release -o /app --no-restore \
    -p:SourceRevisionId="$SOURCE_REVISION_ID"

# ---- runtime ----------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The one directory this application writes to, created and handed to the user that will run —
# while this layer is still root, because after `USER` it cannot chown anything.
#
# Without this the image starts, serves, and answers **500 on every photograph a technician uploads**:
# Docker creates the VOLUME's mount point as root, the non-root app cannot write into it, and
# `Directory.CreateDirectory` at startup succeeds because the directory is already there. Found by
# `make restore-drill` — no test could, because the tests write to a temp directory their own user
# owns. A named volume takes its ownership from the image directory, so this fixes that case too; a
# *bind* mount keeps the host's ownership, which is why the README says to chown it to 1654.
RUN mkdir -p /var/lib/opendispatch/attachments \
    && chown -R $APP_UID:$APP_UID /var/lib/opendispatch/attachments

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
