param([string]$Destination=(Join-Path $PSScriptRoot '../references/probably-stolen-modding'))
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $Destination){throw 'Destination already exists; preserved.'}
git clone --no-checkout https://github.com/dragonpan2/probably-stolen-modding.git $Destination
if($LASTEXITCODE-ne 0){throw 'Official docs clone failed.'}
git -C $Destination checkout f2137eb7bebeecfadca1ccabb0cae3757ea4319f
if($LASTEXITCODE-ne 0){throw 'Pinned documentation commit unavailable.'}
