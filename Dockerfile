# (1) The build environment
FROM mcr.microsoft.com/dotnet/sdk:6.0-jammy as build
WORKDIR /app

# (2) Copy the .csproj and restore; this will cache these layers so they are not run if no changes.
COPY ./dn6-playwright-video-api.csproj ./dn6-playwright-video-api.csproj
RUN dotnet restore

# (3) Copy the application files and build.
COPY ./Program.cs ./Program.cs
RUN dotnet publish ./dn6-playwright-video-api.csproj -o /app/published-app --configuration Release

# (4) The dotnet tagged Playwright environment includes .NET and ffmpeg
FROM mcr.microsoft.com/playwright/dotnet:v1.34.0-jammy as playwright
WORKDIR /app
COPY --from=build /app/published-app /app

# (5) Start our app!
ENTRYPOINT [ "dotnet", "/app/dn6-playwright-video-api.dll" ]