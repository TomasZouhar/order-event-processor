FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["OrderEventProcessor/OrderEventProcessor.csproj", "OrderEventProcessor/"]
RUN dotnet restore "OrderEventProcessor/OrderEventProcessor.csproj"
COPY . .
WORKDIR "/src/OrderEventProcessor"
RUN dotnet build "OrderEventProcessor.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "OrderEventProcessor.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "OrderEventProcessor.dll"]
