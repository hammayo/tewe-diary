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

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

# Pick up SONAR_TOKEN from .env (the IDE run configuration doesn't inherit it).
[ -f .env ] && { set -a; . ./.env; set +a; }

: "${SONAR_TOKEN:?Set SONAR_TOKEN in .env or the environment (SonarCloud: My Account, Security)}"

command -v dotnet-sonarscanner >/dev/null || dotnet tool install --global dotnet-sonarscanner
export PATH="$PATH:$HOME/.dotnet/tools"

dotnet sonarscanner begin \
  /k:"hammayo_tewe-diary" \
  /o:"hammayo" \
  /d:sonar.token="$SONAR_TOKEN" \
  /d:sonar.host.url="https://sonarcloud.io" \
  /d:sonar.cs.opencover.reportsPaths="**/coverage.opencover.xml" \
  /d:sonar.coverage.exclusions="backend/tests/**,backend/tools/**" \
  /d:sonar.exclusions="scripts/**"

dotnet build backend/TeweDiary.sln --no-incremental

if [[ "${1:-}" != "--no-tests" ]]; then
  dotnet test backend/TeweDiary.sln --no-build \
    --collect:"XPlat Code Coverage" \
    -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
fi

dotnet sonarscanner end /d:sonar.token="$SONAR_TOKEN"
