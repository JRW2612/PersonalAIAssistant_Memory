FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["PersonalAIAssistant.Memory.Demo/PersonalAIAssistant.Memory.Demo.csproj", "PersonalAIAssistant.Memory.Demo/"]
COPY ["PersonalAIAssistant.Memory.Business/PersonalAIAssistant.Memory.Business.csproj", "PersonalAIAssistant.Memory.Business/"]
COPY ["PersonalAIAssistant.Memory.Infrastructure/PersonalAIAssistant.Memory.Infrastructure.csproj", "PersonalAIAssistant.Memory.Infrastructure/"]
COPY ["PersonalAIAssistant.Memory.Core/PersonalAIAssistant.Memory.Core.csproj", "PersonalAIAssistant.Memory.Core/"]
COPY ["PersonalAIAssistant.Events/PersonalAIAssistant.Memory.Events.csproj", "PersonalAIAssistant.Events/"]

RUN dotnet restore "PersonalAIAssistant.Memory.Demo/PersonalAIAssistant.Memory.Demo.csproj"

COPY . .
WORKDIR "/src/PersonalAIAssistant.Memory.Demo"
RUN dotnet build "PersonalAIAssistant.Memory.Demo.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PersonalAIAssistant.Memory.Demo.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PersonalAIAssistant.Memory.Demo.dll"]
