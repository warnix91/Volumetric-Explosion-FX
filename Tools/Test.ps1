param([string]$KspRoot = "")
$arguments = @("$PSScriptRoot/project.py", "test")
if ($KspRoot) { $arguments += @("--ksp-root", $KspRoot) }
python @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
