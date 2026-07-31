#!/usr/bin/env bash
# Cheap alternative to the SonarQube MCP tool: hits the REST API directly and
# prints a compact table (file:line severity rule message). The MCP tool pages
# through full issue objects and burns context; this trims to what a commit
# gate actually needs. Run after sonar-scan.ps1 uploads a fresh analysis.
set -euo pipefail

HOST_URL=${1:-http://localhost:9010}
PROJECT_KEY=${2:-silentscan}
PASSWORD=${SONAR_ADMIN_PASSWORD:-'SonarPassword@1'}

curl -s -u "admin:${PASSWORD}" \
  "${HOST_URL}/api/issues/search?componentKeys=${PROJECT_KEY}&resolved=false&ps=200" \
  | jq -r '
      .issues[]
      | [.severity, (.component | sub("^[^:]+:"; "")) + ":" + (.line // 0 | tostring), .rule, .message]
      | @tsv
    ' \
  | sort -k1,1 -t $'\t' \
  | column -t -s $'\t'

echo
echo "Security hotspots to review:"
curl -s -u "admin:${PASSWORD}" \
  "${HOST_URL}/api/hotspots/search?projectKey=${PROJECT_KEY}&status=TO_REVIEW" \
  | jq -r '.hotspots[] | [(.component | sub("^[^:]+:"; "")) + ":" + (.line | tostring), .vulnerabilityProbability, .message] | @tsv' \
  | column -t -s $'\t'

echo
echo "Quality gate:"
curl -s -u "admin:${PASSWORD}" \
  "${HOST_URL}/api/qualitygates/project_status?projectKey=${PROJECT_KEY}" \
  | jq -r '.projectStatus.status'
