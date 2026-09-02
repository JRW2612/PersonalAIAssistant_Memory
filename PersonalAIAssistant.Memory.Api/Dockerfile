# Multi-stage production Dockerfile for PersonalAIAssistant.Memory.Api
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project definition files to optimize layer caching
COPY ["PersonalAIAssistant.Memory.Api/PersonalAIAssistant.Memory.Api.csproj", "PersonalAIAssistant.Memory.Api/"]
COPY ["PersonalAIAssistant.Memory.Business/PersonalAIAssistant.Memory.Business.csproj", "PersonalAIAssistant.Memory.Business/"]
COPY ["PersonalAIAssistant.Memory.Infrastructure/PersonalAIAssistant.Memory.Infrastructure.csproj", "PersonalAIAssistant.Memory.Infrastructure/"]
COPY ["PersonalAIAssistant.Memory.Core/PersonalAIAssistant.Memory.Core.csproj", "PersonalAIAssistant.Memory.Core/"]
COPY ["PersonalAIAssistant.Events/PersonalAIAssistant.Memory.Events.csproj", "PersonalAIAssistant.Events/"]

RUN dotnet restore "PersonalAIAssistant.Memory.Api/PersonalAIAssistant.Memory.Api.csproj"

# Copy entire source tree and build
COPY . .
WORKDIR "/src/PersonalAIAssistant.Memory.Api"
RUN dotnet build "PersonalAIAssistant.Memory.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "PersonalAIAssistant.Memory.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PersonalAIAssistant.Memory.Api.dll"]
