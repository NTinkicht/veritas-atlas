# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore apps/api/VeritasAtlas.Api/VeritasAtlas.Api.csproj
RUN dotnet publish apps/api/VeritasAtlas.Api/VeritasAtlas.Api.csproj \
    --configuration Release --output /app/publish --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000
USER $APP_UID
ENTRYPOINT ["dotnet", "VeritasAtlas.Api.dll"]
