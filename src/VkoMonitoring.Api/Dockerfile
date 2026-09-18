FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY ["src/VkoMonitoring.Agent.Core/VkoMonitoring.Agent.Core.csproj", "src/VkoMonitoring.Agent.Core/"]
COPY ["src/VkoMonitoring.Api/VkoMonitoring.Api.csproj", "src/VkoMonitoring.Api/"]
RUN dotnet restore "src/VkoMonitoring.Api/VkoMonitoring.Api.csproj"

COPY . .
RUN dotnet publish "src/VkoMonitoring.Api/VkoMonitoring.Api.csproj" \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "VkoMonitoring.Api.dll"]
