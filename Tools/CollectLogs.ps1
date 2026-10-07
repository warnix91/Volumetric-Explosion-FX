param([string]$KspRoot = "")
$arguments = @("$PSScriptRoot/project.py", "collect")
if ($KspRoot) { $arguments += @("--ksp-root", $KspRoot) }
python @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
