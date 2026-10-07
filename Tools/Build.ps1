param([string]$KspRoot = "")
$arguments = @("$PSScriptRoot/project.py", "build")
if ($KspRoot) { $arguments += @("--ksp-root", $KspRoot) }
python @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
