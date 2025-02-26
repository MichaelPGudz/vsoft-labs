FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /ToDo

# Copy everything
COPY . ./
# Restore as distinct layers
RUN dotnet restore
# Build and publish a release
RUN dotnet publish -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /ToDo
COPY --from=build /ToDo/out .
ENV ASPNETCORE_ENVIRONMENT=Development
ENTRYPOINT ["dotnet", "TodoApi.dll"]