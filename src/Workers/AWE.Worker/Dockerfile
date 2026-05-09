FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Workers/AWE.Worker/AWE.Worker.csproj", "src/Workers/AWE.Worker/"]
COPY ["src/Core/AWE.Application/AWE.Application.csproj", "src/Core/AWE.Application/"]
COPY ["src/Core/AWE.Domain/AWE.Domain.csproj", "src/Core/AWE.Domain/"]
COPY ["src/BuildingBlocks/AWE.Contracts/AWE.Contracts.csproj", "src/BuildingBlocks/AWE.Contracts/"]
COPY ["src/BuildingBlocks/AWE.Shared/AWE.Shared.csproj", "src/BuildingBlocks/AWE.Shared/"]
COPY ["src/BuildingBlocks/AWE.ServiceDefaults/AWE.ServiceDefaults.csproj", "src/BuildingBlocks/AWE.ServiceDefaults/"]
COPY ["src/Core/AWE.Infrastructure/AWE.Infrastructure.csproj", "src/Core/AWE.Infrastructure/"]
COPY ["src/Core/AWE.WorkflowEngine/AWE.WorkflowEngine.csproj", "src/Core/AWE.WorkflowEngine/"]
COPY ["src/Core/AWE.Sdk/AWE.Sdk.csproj", "src/Core/AWE.Sdk/"]

RUN dotnet restore "src/Workers/AWE.Worker/AWE.Worker.csproj"
COPY . .
WORKDIR "/src/src/Workers/AWE.Worker"
RUN dotnet build "AWE.Worker.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AWE.Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AWE.Worker.dll"]
