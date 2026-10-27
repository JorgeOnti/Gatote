# Etapa 1: Compilación (.NET 8 SDK)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY FractalTicTacToe.Core/FractalTicTacToe.Core.csproj FractalTicTacToe.Core/
COPY FractalTicTacToe.Server/FractalTicTacToe.Server.csproj FractalTicTacToe.Server/
RUN dotnet restore FractalTicTacToe.Server/FractalTicTacToe.Server.csproj

COPY FractalTicTacToe.Core/ FractalTicTacToe.Core/
COPY FractalTicTacToe.Server/ FractalTicTacToe.Server/
RUN dotnet publish FractalTicTacToe.Server/FractalTicTacToe.Server.csproj -c Release -o /app/publish --no-restore

# Etapa 2: Ejecución ligera (.NET 8 Runtime)
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV PORT=7777
ENV SERVER_DATA_DIR=/app/ServerData
RUN mkdir -p /app/ServerData

EXPOSE 7777
ENTRYPOINT ["dotnet", "FractalTicTacToe.Server.dll"]
