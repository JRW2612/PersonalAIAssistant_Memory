FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["PersonalAIAssistant.AiGateway/PersonalAIAssistant.AiGateway.csproj", "PersonalAIAssistant.AiGateway/"]
RUN dotnet restore "PersonalAIAssistant.AiGateway/PersonalAIAssistant.AiGateway.csproj"
COPY PersonalAIAssistant.AiGateway/ PersonalAIAssistant.AiGateway/
WORKDIR /src/PersonalAIAssistant.AiGateway
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
USER app
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "PersonalAIAssistant.AiGateway.dll"]
