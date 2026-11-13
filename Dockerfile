# Build stage: publish the web app.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/BoreholeHub.Web -c Release -o /app

# Runtime stage: the SQLite file boreholehub.db is created in /app at start (EnsureCreated).
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build --chown=app:app /app .
ENV ASPNETCORE_ENVIRONMENT=Production
# Render sets PORT; 8080 is the default for local runs.
CMD ["sh","-c","dotnet BoreholeHub.Web.dll --urls http://0.0.0.0:${PORT:-8080}"]
