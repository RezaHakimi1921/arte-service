# Build from the repository root: docker build -f src/Arte.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/Arte.Core/Arte.Core.csproj src/Arte.Core/
COPY src/Arte.Api/Arte.Api.csproj src/Arte.Api/
RUN dotnet restore src/Arte.Api/Arte.Api.csproj
COPY src/ src/
RUN dotnet publish src/Arte.Api/Arte.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0
# Built-in non-root user of the .NET images.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Arte.Api.dll"]
