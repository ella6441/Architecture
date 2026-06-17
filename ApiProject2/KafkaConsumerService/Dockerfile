FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["KafkaConsumerService/KafkaConsumerService.csproj", "KafkaConsumerService/"]
RUN dotnet restore "./KafkaConsumerService/KafkaConsumerService.csproj"
COPY . .
WORKDIR "/src/KafkaConsumerService"
RUN dotnet build "./KafkaConsumerService.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./KafkaConsumerService.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "KafkaConsumerService.dll"]
