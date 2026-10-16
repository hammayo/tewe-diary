#!/usr/bin/env bash
#  
# Runs the SonarCloud analysis locally: begin, build, test with coverage, end.
# Mirrors .github/workflows/sonarcloud.yml. Needs Java 17+ and Docker (Testcontainers).
#
# Usage:
# Turn off automatic code analysis: https://sonarcloud.io/project/analysis_method?id=hammayo_tewe-diary
# Generate PAT token: https://sonarcloud.io/account/access-tokens
#   SONAR_TOKEN=<token> scripts/sonar-scan.sh
#   SONAR_TOKEN=<token> scripts/sonar-scan.sh --no-tests   # issues only, no coverage
set -euo pipefail

: "${SONAR_TOKEN:?Set SONAR_TOKEN (SonarCloud: My Account, Security)}"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

command -v dotnet-sonarscanner >/dev/null || dotnet tool install --global dotnet-sonarscanner
export PATH="$PATH:$HOME/.dotnet/tools"

dotnet sonarscanner begin \
  /k:"hammayo_tewe-diary" \
  /o:"hammayo" \
  /d:sonar.token="$SONAR_TOKEN" \
  /d:sonar.host.url="https://sonarcloud.io" \
  /d:sonar.cs.opencover.reportsPaths="**/coverage.opencover.xml" \
  /d:sonar.coverage.exclusions="Tests/**,Ops.Tools/**"

dotnet build TeweRestApi.sln --no-incremental

if [[ "${1:-}" != "--no-tests" ]]; then
  dotnet test TeweRestApi.sln --no-build \
    --collect:"XPlat Code Coverage" \
    -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
fi

dotnet sonarscanner end /d:sonar.token="$SONAR_TOKEN"
