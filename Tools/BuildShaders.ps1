param([ValidateSet('windows','mac')][string]$Target='windows', [string]$UnityEditor='')
$arguments=@("$PSScriptRoot/shaders.py",'--target',$Target)
if ($UnityEditor) { $arguments+=@('--unity-editor',$UnityEditor) }
python @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
