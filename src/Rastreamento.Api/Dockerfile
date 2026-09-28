# Build da Rastreamento.Api (contexto = raiz do repositório).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/LogisticaPlataforma.sln ./
COPY src/Rastreamento.Api/ ./Rastreamento.Api/
COPY src/Shared.Kernel/ ./Shared.Kernel/
RUN dotnet publish ./Rastreamento.Api/Rastreamento.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Rastreamento.Api.dll"]
